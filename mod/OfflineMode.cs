using MelonLoader.Utils;

namespace PLATiNALAB.Mod;

/// <summary>
/// 완전 오프라인 독립 사본 모드 (config.txt OfflineMode=1). 기본은 꺼짐.
///
/// 켜면: 스팀 SDK·로그인·서버 오류 팝업 우회(SteamBypassPatches), 모든 곡의 로컬 세션·결과(OfflineSession),
/// 씬 전환 커튼 정리(CurtainBypassPatches), 공식 곡 소유 판정의 오프라인 재현(SongOwnership)이 동작한다.
/// 끄면: v1.3.0과 같다 — 스팀·서버는 게임 원본대로, 서버 우회는 커스텀 곡만.
///
/// 설계 이유 (19차 감사): decf3b7에서는 이 우회가 모든 사용자에게 무조건 걸려서, 스팀으로 정상 실행해도
/// 공식 곡 기록이 서버로 가지 않고, 게임 메뉴의 종료(Application.Quit)가 막히고, 소유하지 않은 유료 DLC 곡까지 열렸다.
/// 스팀 초기화는 게임 시작 직후 한 번뿐이라 값은 시작 시 한 번만 읽는다 (게임 도중 config를 바꾸면 재시작 후 적용).
/// </summary>
internal static class Offline
{
    public static bool Enabled { get; private set; }
    public static bool Initialized { get; private set; }

    public static string Nickname { get; private set; } = ModConfig.DefaultOfflineNickname;
    public static string UserId { get; private set; } = ModConfig.DefaultOfflineUserId;

    /// <summary>UserId를 CSteamID 값으로. 숫자가 아니면 0.</summary>
    public static ulong SteamId64 => ulong.TryParse(UserId, out var v) ? v : 0UL;

    public static void Init()
    {
        if (Initialized) return;
        Enabled = ModConfig.OfflineMode;
        Nickname = string.IsNullOrWhiteSpace(ModConfig.OfflineNickname) ? ModConfig.DefaultOfflineNickname : ModConfig.OfflineNickname;
        UserId = string.IsNullOrWhiteSpace(ModConfig.OfflineUserId) ? ModConfig.DefaultOfflineUserId : ModConfig.OfflineUserId;
        Initialized = true;
        s_initTick = Environment.TickCount64;
        SongOwnership.Load();
        if (Enabled)
            Core.Log.Msg($"[Offline] 오프라인 독립 실행 모드 켜짐 (닉네임 '{Nickname}', 공식 곡 소유 기록 {SongOwnership.Count}곡)");
        else
            Core.Log.Msg($"[Offline] 온라인 모드 (스팀·서버는 게임 원본, 공식 곡 소유 기록 {SongOwnership.Count}곡 → 오프라인 모드에서 재현)");
    }

    // --- Application.Quit 정책 ---
    // 오프라인에서 게임이 서버·로그인 실패로 스스로 종료하는 것은 막되, 사용자가 메뉴에서 누른 종료까지 막지는 않는다.
    // 종료를 부르는 오류 팝업(점검·미로그인·치명적 서버 오류)은 팝업 단계에서 이미 막으므로, 남은 Quit 호출은
    //  - 로그인 완료 전(부팅 중) 또는 우회한 오류 직후 15초 안 → 게임이 오류로 끄려는 것으로 보고 막는다.
    //  - 그 밖 → 사용자의 종료로 보고 허용한다.
    private const long QuitBlockWindowMs = 15_000;
    private static long s_lastBlockedErrorTick = long.MinValue / 2;
    private static volatile bool s_bootCompleted;
    private static long s_initTick;

    /// <summary>
    /// 로그인 완료 신호(LoginAndLoadProfile 우회·SongSelectDirector.Awake)를 둘 다 못 받는 경우의 안전장치 (20차).
    /// 게임 업데이트로 두 훅이 모두 꺼지면 s_bootCompleted가 영영 서지 않아 메뉴 종료가 계속 막혔다.
    /// 시작 후 이 시간이 지나면 부팅이 끝난 것으로 본다.
    /// </summary>
    private const long BootTimeoutMs = 180_000;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> s_logged = new();

    /// <summary>
    /// 같은 우회 로그를 한 번만 남긴다 (20차). 소셜·보상·해금 더미 응답은 선곡 화면에서 곡을 넘길 때마다 불릴 수 있어
    /// 매번 Msg를 남기면 Latest.log 쓰기가 병목이 된다.
    /// </summary>
    public static void LogOnce(string key, string message)
    {
        if (s_logged.TryAdd(key, 0)) Core.Log.Msg(message + " (이후 같은 로그 생략)");
    }

    public static void NoteBlockedError() => Interlocked.Exchange(ref s_lastBlockedErrorTick, Environment.TickCount64);

    public static void NoteBootCompleted() => s_bootCompleted = true;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long[]> s_warnState = new();

    /// <summary>
    /// 오류 팝업 차단 경고를 키마다 10초에 한 번만 남기고, 그 사이 생략한 횟수를 함께 적는다 (20차).
    /// 오프라인에서는 모든 API 호출이 실패하므로 APIServerError·ShowErrorForCode가 연달아 불린다.
    /// </summary>
    public static void WarnThrottled(string key, string message)
    {
        const long WindowMs = 10_000;
        // 람다 GetOrAdd는 Il2Cppmscorlib 참조 환경에서 CS0656(NullableAttribute)을 낸다 → TryGetValue + GetOrAdd(값)
        if (!s_warnState.TryGetValue(key, out var st)) st = s_warnState.GetOrAdd(key, new long[] { long.MinValue / 2, 0 });
        long now = Environment.TickCount64;
        lock (st)
        {
            if (now - st[0] < WindowMs) { st[1]++; return; }
            long skipped = st[1];
            st[0] = now;
            st[1] = 0;
            Core.Log.Warning(skipped > 0 ? $"{message} (직전 10초간 같은 경고 {skipped}건 생략)" : message);
        }
    }

    public static bool ShouldBlockQuit(out string reason)
    {
        reason = null;
        if (!Enabled) return false;
        if (!s_bootCompleted)
        {
            if (Environment.TickCount64 - s_initTick < BootTimeoutMs) { reason = "로그인 완료 전"; return true; }
            s_bootCompleted = true;
            Core.Log.Warning($"[Offline] 로그인 완료 신호 없이 {BootTimeoutMs / 1000}초가 지나 부팅 완료로 간주합니다 (로그인 우회 훅이 꺼졌는지 [HookGuard]/[SafePatcher] 로그 확인).");
        }
        long since = Environment.TickCount64 - Interlocked.Read(ref s_lastBlockedErrorTick);
        if (since < QuitBlockWindowMs) { reason = $"서버 오류 우회 {since / 1000}초 후"; return true; }
        return false;
    }
}

/// <summary>
/// 공식 곡 소유·해금 판정 기록 (UserData\PLATiNALAB\song_ownership.json). 저장소 로직은 <see cref="SongOwnershipStore"/>.
/// 온라인 모드에서 게임이 실제로 돌려준 값을 기록하고(값이 바뀔 때만), 오프라인 모드에서 재현한다.
/// 판정 게터는 게임 스레드에서 자주 불리므로 저장은 메인 스레드(Core.OnUpdate)에서 변경 후 3초 뒤 한 번, 종료 시 한 번 한다.
/// </summary>
internal static class SongOwnership
{
    private const long SaveDelayMs = 3_000;
    private static SongOwnershipStore s_store = new();
    private static volatile bool s_dirty;
    private static long s_dirtySince;
    private static int s_missingLogged;

    private static string FilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "PLATiNALAB", "song_ownership.json");

    public static int Count => s_store.Count;

    public static void Load()
    {
        try
        {
            var map = JsonMapFile.LoadWithRecovery(FilePath, w => Core.Log.Warning($"[Ownership] {w}"), out _, minValue: 1);
            s_store = new SongOwnershipStore(map);
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[Ownership] 소유 기록 읽기 실패 (빈 기록으로 진행): {e.Message}");
        }
    }

    /// <summary>
    /// 판정 훅 Prefix에서 부른다. 오프라인이면 기록값(없으면 잠김)을 <paramref name="result"/>에 넣고 false(원본 생략).
    /// 온라인이면 원본을 그대로 실행하고, Postfix가 기록하도록 곡 ID를 <paramref name="songId"/>로 넘긴다.
    /// </summary>
    public static bool Prefix(string id, int kind, ref bool result, out string songId)
    {
        songId = null;
        if (!Offline.Enabled)
        {
            songId = id;
            return true;
        }
        result = s_store.Resolve(id, kind, out bool known);
        if (!known && id != null && Interlocked.Increment(ref s_missingLogged) == 1)
            Core.Log.Warning($"[Ownership] 소유 기록이 없는 공식 곡('{id}' 등)은 잠긴 상태로 표시합니다. " +
                             "OfflineMode=0으로 스팀 온라인 실행 후 선곡 화면을 한 번 열면 보유한 곡이 기록됩니다.");
        return false;
    }

    /// <summary>판정 훅 Postfix (온라인). 원본이 돌려준 값을 기록한다.</summary>
    public static void Record(string songId, int kind, bool value)
    {
        if (songId == null || Offline.Enabled) return;
        if (s_store.Record(songId, kind, value))
        {
            Interlocked.Exchange(ref s_dirtySince, Environment.TickCount64);
            s_dirty = true;
        }
    }

    /// <summary>메인 스레드 매 프레임. 평소에는 volatile 읽기 하나로 끝난다.</summary>
    public static void Tick()
    {
        if (!s_dirty) return;
        if (Environment.TickCount64 - Interlocked.Read(ref s_dirtySince) < SaveDelayMs) return;
        Flush();
    }

    public static void Flush()
    {
        if (!s_dirty) return;
        s_dirty = false;
        if (!JsonMapFile.Write(FilePath, s_store.Snapshot(), w => Core.Log.Warning($"[Ownership] {w}")))
        {
            // 매 프레임 재시도하지 않도록 대기 시간을 다시 잰다
            Interlocked.Exchange(ref s_dirtySince, Environment.TickCount64);
            s_dirty = true;
        }
    }
}

/// <summary>
/// OfflineMode=1일 때만 동작해야 하는 패치 클래스 표시. 하네스 [Offline]이 이 클래스의 Prefix/Postfix가
/// Offline.Enabled(또는 Offline.ShouldBlockQuit)를 직접 확인하는지, 스팀·플랫폼 타입을 겨냥한 패치에 이 표시가 있는지 검사한다.
/// (게이트를 빠뜨리면 스팀으로 정상 실행하는 사용자의 로그인·기록 전송이 조용히 막힌다)
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
internal sealed class OfflineOnlyAttribute : Attribute { }
