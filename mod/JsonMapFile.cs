using System.Text.Json;

namespace PLATiNALAB.Mod;

/// <summary>
/// UserData\PLATiNALAB 아래의 작은 { "문자열": 정수 } JSON 파일(song_ids.json, song_ownership.json) 공용 읽기/쓰기.
/// 게임·MelonLoader 타입을 쓰지 않아 오프라인 하네스(PureLogicTests)가 그대로 검증한다.
/// - 쓰기: 임시 파일에 쓴 뒤 교체한다 (도중 종료로 본 파일이 잘리지 않게). 직전 본은 .bak으로 남는다.
/// - 읽기: 본 파일이 깨졌으면 .corrupt로 옮겨 남기고 .bak으로 복구한다 (복구했으면 본 파일도 바로 되살린다).
/// </summary>
internal static class JsonMapFile
{
    public enum LoadResult { Ok, RecoveredFromBackup, Reset }

    /// <summary>파일이 없으면 빈 맵으로 성공. 내용이 깨졌으면 false. 키가 비었거나 값이 <paramref name="minValue"/> 미만인 항목은 버린다.</summary>
    public static bool TryRead(string path, out Dictionary<string, int> map, out string error, int minValue = int.MinValue)
    {
        map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        error = null;
        try
        {
            if (!File.Exists(path)) return true;
            var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
            if (parsed != null)
                foreach (var (k, v) in parsed)
                    if (!string.IsNullOrEmpty(k) && v >= minValue) map[k] = v;
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>
    /// 읽기 + 손상 복구. 본 파일이 깨졌으면 .corrupt로 옮기고 .bak에서 복구한다. 둘 다 안 되면 빈 맵(<see cref="LoadResult.Reset"/>).
    /// <paramref name="warn"/>에는 사람이 읽을 경고가 들어간다 (로거를 받지 않아 하네스에서도 돈다).
    /// </summary>
    public static Dictionary<string, int> LoadWithRecovery(string path, Action<string> warn, out LoadResult result, int minValue = int.MinValue)
    {
        result = LoadResult.Ok;
        if (TryRead(path, out var map, out var error, minValue)) return map;
        var name = Path.GetFileName(path);
        warn?.Invoke($"{name} 읽기 실패: {error}");
        try { File.Move(path, path + ".corrupt", overwrite: true); }
        catch (Exception e) { warn?.Invoke($"손상된 {name} 보관 실패: {e.Message}"); }
        if (TryRead(path + ".bak", out map, out _, minValue) && map.Count > 0)
        {
            warn?.Invoke($"{name}.bak에서 항목 {map.Count}개를 복구했습니다 (손상 파일: {name}.corrupt)");
            Write(path, map, warn);
            result = LoadResult.RecoveredFromBackup;
            return map;
        }
        warn?.Invoke($"복구할 백업이 없어 {name}을(를) 새로 시작합니다 (손상 파일: {name}.corrupt)");
        result = LoadResult.Reset;
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    public static bool Write(string path, IReadOnlyDictionary<string, int> map, Action<string> warn)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true });
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (!File.Exists(path)) File.Move(tmp, path);
            else
            {
                try { File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true); }
                catch (Exception e) when (e is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
                {
                    // File.Replace를 지원하지 않는 볼륨(FAT32·네트워크 드라이브·일부 동기화 폴더)이나 .bak이 잠긴 경우 (20차):
                    // 백업은 복사로 남기고 본 파일은 덮어쓰기 이동으로 교체한다 (원자성은 약하지만 저장 자체는 성공시킨다).
                    try { File.Copy(path, path + ".bak", overwrite: true); } catch { /* 백업 실패는 저장을 막지 않는다 */ }
                    File.Move(tmp, path, overwrite: true);
                    warn?.Invoke($"{Path.GetFileName(path)}: 교체 저장(File.Replace) 불가 → 덮어쓰기로 저장 ({e.GetType().Name})");
                }
            }
            return true;
        }
        catch (Exception e)
        {
            warn?.Invoke($"{Path.GetFileName(path)} 저장 실패: {e.Message}");
            return false;
        }
    }
}
