using System.Text.Json.Nodes;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppPlatinaLab.DataModel.SongCategory;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPlatinaLab.Main.Catalog;
using Il2CppPlatinaLab.Main.Catalog.Scheme.Song;
using Il2CppPlatinaLab.Main.Select.Category;
using Il2CppPlatinaLab.Main.Select.Category.Cache;
using MelonLoader.Utils;

namespace PLATiNALAB.Mod;

/// <summary>
/// hwa\ 폴더의 곡 폴더 하나 = 커스텀 곡 하나.
/// info.txt(메타), chart_{난이도}.json 또는 chart.json(PPP 차트), music.ogg(미리듣기+BGM), video.mp4(BGA), 썸네일.png(커버).
/// </summary>
internal sealed class CustomSong
{
    public string Id;
    public int Number;
    public string Folder;
    public string Title;
    public string Artist = "Unknown";
    public string Bpm;
    public string Patterner = "hwa";
    /// <summary>BMS 레인 1~4에 대응하는 채널 (info.txt `lanes = 16,11,12,13`). 비면 BmsConverter 기본 순서.</summary>
    public readonly List<string> Lanes = new();
    public readonly List<CustomChart> Charts = new();

    private static readonly char[] KeyValueSeparators = { ':', '=' };
    private static readonly string[] MusicNames = { "music.ogg", "audio.ogg", "song.ogg", "bgm.ogg", "音乐.ogg", "音频.ogg" };
    private static readonly string[] VideoNames = { "video.mp4", "bga.mp4", "bg.mp4", "视频.mp4", "背景.mp4" };
    private static readonly string[] ThumbnailNames =
    {
        "썸네일.png", "thumbnail.png", "cover.png", "jacket.png",
        "封面.png", "曲绘.png", "封面.jpg", "曲绘.jpg",
        "썸네일.jpg", "thumbnail.jpg", "cover.jpg", "jacket.jpg",
    };

    // 커버(CoverManager.GetCoverSprite)·음원(SoundData.FromHEAudioClip)·BMS 탐색 훅은 호출이 잦아서,
    // 매번 디스크 조회를 반복하면 선곡·플레이 로딩에 디스크 I/O가 몰린다.
    // 확인 결과를 잠깐 기억하고, 곡 시작·미리듣기 때 RefreshMedia로 다시 확인한다 (파일을 추가·교체해도 반영됨).
    private readonly MediaSlot _music, _video, _thumbnail;

    public CustomSong()
    {
        _music = new MediaSlot(() => FirstExisting(MusicNames));
        _video = new MediaSlot(() => FirstExisting(VideoNames));
        _thumbnail = new MediaSlot(() => FirstExisting(ThumbnailNames));
    }

    public string MusicPath => _music.Get();
    public string VideoPath => _video.Get();
    public string ThumbnailPath => _thumbnail.Get();

    public void RefreshMedia()
    {
        _music.Invalidate();
        _video.Invalidate();
        _thumbnail.Invalidate();
        foreach (var c in Charts) c.InvalidateMedia();
    }

    public string FirstExisting(params string[] names)
    {
        foreach (var n in names)
        {
            var p = Path.Combine(Folder, n);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// info.txt: 한 줄에 "키 : 값" 또는 "키 = 값". 사용자 실제 형식 예:
    ///   곡 제목 : どりーむもーど / 아티스트 : 화영왕 / 난이도 : easy, normal, hard / easy = 8
    /// </summary>
    public static CustomSong Load(string folder, int number)
    {
        var song = new CustomSong
        {
            Id = $"{CustomSongs.IdPrefix}{number:D3}",
            Number = number,
            Folder = folder,
            Title = Path.GetFileName(folder),
        };
        var difficulties = new List<string>();
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool hasTitle = false, hasArtist = false;

        var info = Path.Combine(folder, "info.txt");
        if (File.Exists(info))
        {
            foreach (var raw in EncodingHelper.ReadAllLinesSafe(info))
            {
                var line = raw.Trim().TrimStart('﻿');
                int sep = line.IndexOfAny(KeyValueSeparators);
                if (line.StartsWith("#") || sep <= 0) continue;
                var key = line[..sep].Trim().ToLowerInvariant().Replace(" ", "");
                var value = line[(sep + 1)..].Trim();
                switch (key)
                {
                    case "곡제목": case "제목": case "title":
                    case "歌名": case "曲名": case "标题":
                        song.Title = TextSanitizer.Sanitize(value); hasTitle = true; break;
                    case "아티스트": case "artist":
                    case "曲师": case "作曲": case "艺术家": case "作者":
                        song.Artist = TextSanitizer.Sanitize(value); hasArtist = true; break;
                    case "bpm": song.Bpm = value; break;
                    case "패턴제작자": case "채보": case "patterner":
                    case "谱师": case "谱面": case "谱面制作者": case "作谱":
                        song.Patterner = TextSanitizer.Sanitize(value); break;
                    case "레인": case "lanes":
                    case "轨道": case "键位": case "按键":
                        song.Lanes.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(x => x.Trim().ToUpperInvariant()));
                        break;
                    case "난이도": case "difficulty": case "difficulties":
                    case "难度":
                        difficulties.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(d => NormalizeDifficulty(d)));
                        break;
                    default:
                        var normKey = NormalizeDifficulty(key);
                        if (int.TryParse(value, out var lv)) levels[normKey] = lv;
                        break;
                }
            }
        }

        if (difficulties.Count == 0) difficulties.Add("easy");

        // info.txt에 없는 메타(#TITLE, #ARTIST, #BPM, #PLAYLEVEL)는 같은 폴더 BMS 헤더에서 한 번에 채운다.
        var firstBms = BmsConverter.FindFor(folder, difficulties[0]);
        BmsConverter.BmsChart firstHeader = null;
        if (firstBms != null && (!hasTitle || !hasArtist || song.Bpm == null || (!levels.ContainsKey(difficulties[0]) && !levels.ContainsKey("level"))))
        {
            firstHeader = BmsConverter.ParseHeader(firstBms);
            if (!hasTitle && !string.IsNullOrWhiteSpace(firstHeader.Title)) song.Title = firstHeader.Title;
            if (!hasArtist && !string.IsNullOrWhiteSpace(firstHeader.Artist)) song.Artist = firstHeader.Artist;
            if (song.Bpm == null && firstHeader.Bpm > 0)
                song.Bpm = firstHeader.Bpm.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        for (int i = 0; i < difficulties.Count && i < 10; i++)
        {
            var d = difficulties[i];
            int lv;
            if (levels.TryGetValue(d, out var explicitLv))
                lv = explicitLv;
            else if (levels.TryGetValue("level", out var defaultLv))
                lv = defaultLv;
            else
            {
                var bmsPath = BmsConverter.FindFor(folder, d);
                var hdr = bmsPath == null ? null :
                    string.Equals(bmsPath, firstBms, StringComparison.OrdinalIgnoreCase) && firstHeader != null ? firstHeader :
                    BmsConverter.ParseHeader(bmsPath);
                lv = hdr != null && hdr.PlayLevel > 0 ? hdr.PlayLevel : 1;
            }
            song.Charts.Add(new CustomChart
            {
                Song = song,
                Difficulty = d,
                Level = lv,
                PatternIndex = CustomSongs.PatternIndexBase + number * 10 + i,
            });
        }
        return song;
    }

    public static string NormalizeDifficulty(string d) => BmsConverter.NormalizeDifficulty(d);
}

internal sealed class MediaSlot
{
    private const long TtlMs = 5000;
    // record는 Nullable 특성을 요구해 이 프로젝트에서 CS0656이 난다 (AGENTS.md Dead End) → 일반 클래스
    private sealed class Resolved
    {
        public readonly string Path;
        public readonly long At;
        public Resolved(string path, long at) { Path = path; At = at; }
    }
    private readonly Func<string> _resolver;
    private volatile Resolved _resolved; // 여러 스레드(로딩·게임 루프)에서 읽으므로 경로와 시각을 한 객체로 바꿔 끼운다

    public MediaSlot(Func<string> resolver) => _resolver = resolver;

    public string Get()
    {
        long now = Environment.TickCount64;
        var r = _resolved;
        if (r != null && now - r.At < TtlMs) return r.Path;
        var path = _resolver();
        _resolved = new Resolved(path, now);
        return path;
    }

    public void Invalidate() => _resolved = null;
}

/// <summary>커스텀 곡의 난이도 하나 = 게임 패턴 하나.</summary>
internal sealed class CustomChart
{
    public CustomSong Song;
    /// <summary>info.txt에 적힌 난이도 이름 (차트 파일명에 사용)</summary>
    public string Difficulty;
    /// <summary>게임 _difficultyTypeInfos에 실제로 있는 난이도 키 (Inject에서 결정)</summary>
    public string GameDifficulty;
    public int Level;
    public int PatternIndex;
    public PatternInfo Pattern;
    private MediaSlot _bms;

    /// <summary>곡 폴더에 BMS가 있으면 이걸 변환해서 쓴다 (JSON보다 우선). 파일명에 난이도가 들어간 BMS → 없으면 첫 BMS. 5초 캐시 + RefreshMedia로 갱신.</summary>
    public string BmsPath => (_bms ?? InitBmsSlot()).Get();

    private MediaSlot InitBmsSlot()
    {
        Interlocked.CompareExchange(ref _bms, new MediaSlot(() => BmsConverter.FindFor(Song.Folder, Difficulty)), null);
        return _bms;
    }
    public void InvalidateMedia() => _bms?.Invalidate();
    /// <summary>BMS가 없을 때: chart_{난이도}.json 우선, 없으면 chart.json. 둘 다 없으면 chart_{난이도}.json으로 템플릿을 만든다.</summary>
    public string ChartPath => Song.FirstExisting($"chart_{Difficulty}.json", "chart.json") ?? Path.Combine(Song.Folder, $"chart_{Difficulty}.json");
    public string Key => $"{Song.Id}_{Difficulty}";
}

/// <summary>
/// 선곡 화면에 "custom" 카테고리를 추가하고, hwa\ 폴더의 곡들을 기존 곡을 복제해 넣는다.
/// - 곡 데이터(SongInfo/PatternInfo/SongMetaDataInfo)는 기반 패턴(BasePatternKey)의 것을 복제.
/// - 패턴 FilePath는 기반 곡 그대로 둬서 Addressables 차트 로드가 성공하게 하고,
///   실제 차트는 CustomCharts의 Parse 후킹에서 곡 폴더의 chart.json으로 교체된다.
/// - PatternIndex는 존재하지 않는 값(990001~)을 써서 서버 기록/랭킹에 원곡 이름으로 점수가 올라가지 않게 한다.
/// </summary>
internal static class CustomSongs
{
    public const string CategoryTag = "custom";
    public const string IdPrefix = "hwa_";
    public const int PatternIndexBase = 990000;
    public const string BasePatternKey = "FourLines_Easy";

    public static readonly string RootDir = Path.Combine(MelonEnvironment.GameRootDirectory, "hwa");
    public static readonly List<CustomSong> All = new();

    /// <summary>지금 플레이 중인 커스텀 패턴 (RequestGameStart에서 설정, 일반 곡이면 null).</summary>
    public static CustomChart Current;

    /// <summary>기반 패턴의 리소스 파일명. 커스텀 패턴도 이 리소스를 로드해야 Parse 후킹까지 도달한다.</summary>
    internal static string s_baseFileName;
    /// <summary>
    /// 기반 곡 SongInfo. PlayDirector.LoadGame은 차트 등 리소스 주소를 SongInfo.Id로 만든다
    /// (실측: Concat(prefix, SongInfo.Id, sep, GetPatternFileName()) → Addressables.LoadAssetAsync).
    /// 그래서 Addressables 키의 커스텀 ID를 이 곡 ID로 치환한다 (AddressableKeyRedirect).
    /// </summary>
    internal static SongInfo s_baseSong;
    /// <summary>s_baseSong.Id를 관리 문자열로 한 번만 받아 둔다 (Locate·음원 훅에서 매번 IL2CPP 문자열을 변환하지 않도록).</summary>
    internal static string s_baseSongId;

    /// <summary>커스텀 곡이 게임 데이터에 들어갔는지. 그 전에는 커스텀 SongInfo가 없으므로 SongInfo 훅이 Id를 읽지 않고 넘어간다.</summary>
    public static volatile bool Injected;

    // 곡 ID·패턴 번호·차트 키 → 곡/차트 사전. 선곡·판정 훅이 일반 곡마다 조회하므로 목록 순회 대신 사전을 쓴다.
    // Scan에서 새 사전을 만들어 통째로 바꿔 끼운다 (다른 스레드는 읽기만 함).
    private static Dictionary<string, CustomSong> s_byId = new();
    private static Dictionary<int, CustomChart> s_byPatternIndex = new();
    private static Dictionary<string, CustomChart> s_byKey = new();

    public static bool IsCustomId(string songId) => songId != null && songId.StartsWith(IdPrefix, StringComparison.Ordinal);
    public static CustomSong ById(string songId) => songId != null && s_byId.TryGetValue(songId, out var s) ? s : null;

    /// <summary>
    /// 문자열(리소스 키 등)에 든 커스텀 곡 ID. 가장 긴 ID를 고른다 — 곡이 1000개를 넘으면
    /// "hwa_1000"이 "hwa_100"도 포함하므로 처음 맞는 것을 쓰면 다른 곡으로 치환된다.
    /// </summary>
    public static CustomSong FindIdIn(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        CustomSong best = null;
        foreach (var s in All)
            if (s.Id != null && (best == null || s.Id.Length > best.Id.Length) && text.Contains(s.Id, StringComparison.Ordinal))
                best = s;
        return best;
    }
    public static CustomChart ByPatternIndex(int index) => s_byPatternIndex.TryGetValue(index, out var c) ? c : null;

    public static CustomChart ByKey(string key) => key != null && s_byKey.TryGetValue(key, out var c) ? c : null;

    // 인터롭 게터(PatternIndex, Id)는 il2cpp_runtime_invoke를 거치고 Id는 관리 문자열까지 만든다.
    // 모든 곡·패턴에서 도는 판정 훅은 백킹 필드를 직접 읽고, 처음 32건은 게터 결과와 맞춰 본다 (NativeField 설명 참고).
    private static readonly NativeField s_patternIndexField = new("PatternInfo.PatternIndex", "<PatternIndex>k__BackingField");
    private static readonly NativeField s_songIdField = new("SongInfo.Id", "<Id>k__BackingField");

    /// <summary>PatternInfo.PatternIndex를 직접 읽으므로 별도 메서드로 둔다 (호출부 try가 바인딩 실패를 잡을 수 있게).</summary>
    [MethodImpl(MethodImplOptions.NoInlining), Guarded("PatternInfo.PatternIndex", required: true)]
    public static CustomChart Of(PatternInfo info)
    {
        // SongInfo·PatternInfo는 게임 record라 `== null`이 인터롭에서 op_Equality(il2cpp_runtime_invoke)가 된다 → `is null` (하네스 [Body] 검사)
        if (info is null || s_byPatternIndex.Count == 0) return null;
        if (s_patternIndexField.TryReadInt32(info.Pointer, out int native))
        {
            if (!s_patternIndexField.NeedsSelfCheck) return ByPatternIndex(native);
            int managed = info.PatternIndex;
            s_patternIndexField.ReportCheck(native == managed, $"{native} != {managed}");
            return ByPatternIndex(managed);
        }
        return ByPatternIndex(info.PatternIndex);
    }

    public static bool IsCustom(PatternInfo info) => Of(info) != null;

    private const string SongIdHook = "SongInfo.Id";

    /// <summary>
    /// SongInfo 판정 훅(IsLock/IsAvailable 등)용. 이 훅들은 모든 곡에서 자주 불리므로
    /// 주입 전에는 Id를 읽지 않고, Id 읽기는 별도 메서드로 격리한다 (업데이트로 Id가 바뀌어도 일반 곡 판정은 원본대로).
    /// 주입 후에는 Id 백킹 필드의 IL2CPP 문자열에서 접두사(hwa_)를 직접 확인한다 (게터 호출·문자열 할당 없음).
    /// </summary>
    public static bool IsCustomSong(SongInfo song)
    {
        if (!Injected || song is null || HookGuard.IsDisabled(SongIdHook)) return false;
        try
        {
            if (s_songIdField.TryReadPointer(song.Pointer, out var idPtr))
            {
                if (!s_songIdField.NeedsSelfCheck) return NativeStrings.StartsWith(idPtr, IdPrefix);
                // 접두사 판정만 비교하면 일반 곡(모두 false)으로는 잘못된 오프셋을 못 잡는다 → 문자열 전체를 비교
                var managed = ReadSongId(song);
                s_songIdField.ReportCheck(NativeStrings.EqualsManaged(idPtr, managed), $"'{managed}'");
                return IsCustomId(managed);
            }
            return IsCustomId(ReadSongId(song));
        }
        catch (Exception e)
        {
            HookGuard.Fail(SongIdHook, e, required: true);
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("SongInfo.Id", required: true)]
    private static string ReadSongId(SongInfo song) => song.Id;

    /// <summary>EncryptedBool 암시 변환을 훅 본문 밖으로 뺀다 (변환 연산자가 바뀌어도 훅 본문 JIT가 실패하지 않게).</summary>
    [MethodImpl(MethodImplOptions.NoInlining), Guarded("EncryptedBool")]
    internal static Il2CppPlatinaLab.Common.EncryptedPrimitives.EncryptedBool EncryptedFalse() => false;

    public static void Scan()
    {
        Directory.CreateDirectory(RootDir);
        var folders = Directory.GetDirectories(RootDir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (folders.Count == 0)
        {
            var sample = Path.Combine(RootDir, "sample");
            Directory.CreateDirectory(sample);
            File.WriteAllText(Path.Combine(sample, "info.txt"),
                "# 커스텀 곡 정보 (key=value)\ntitle=Sample Song\nartist=hwa\nbpm=120\nlevel=1\npatterner=hwa\n");
            folders.Add(sample);
        }
        All.Clear();
        // 곡 번호(= 곡 ID·패턴 번호)를 UserData\PLATiNALAB\song_ids.json에 폴더명 기준으로 영속화해,
        // 새 폴더를 추가하거나 앞의 폴더를 지워도 기존 곡의 hwa_NNN·PatternIndex가 밀리지 않게 한다.
        var idMap = LoadSongIdMap();
        int nextNumber = idMap.Count > 0 ? idMap.Values.Max() + 1 : 1;
        bool mapDirty = false;
        foreach (var folder in folders)
        {
            var name = Path.GetFileName(folder);
            if (!idMap.TryGetValue(name, out var number))
            {
                number = nextNumber++;
                idMap[name] = number;
                mapDirty = true;
            }
            try { All.Add(CustomSong.Load(folder, number)); }
            catch (Exception e) { Core.Log.Error($"[CustomSongs] 곡 폴더 '{name}' 읽기 실패, 이 곡만 건너뜁니다: {e.Message}"); }
        }
        if (mapDirty) SaveSongIdMap(idMap);
        var byId = new Dictionary<string, CustomSong>(StringComparer.Ordinal);
        var byIndex = new Dictionary<int, CustomChart>();
        var byKey = new Dictionary<string, CustomChart>(StringComparer.Ordinal);
        foreach (var s in All)
        {
            byId[s.Id] = s;
            foreach (var c in s.Charts)
            {
                byIndex[c.PatternIndex] = c;
                byKey[c.Key] = c;
            }
        }
        s_byId = byId;
        s_byPatternIndex = byIndex;
        s_byKey = byKey;
        foreach (var s in All)
            Core.Log.Msg($"[CustomSongs] {s.Id} '{s.Title}' ({Path.GetFileName(s.Folder)}) music={s.MusicPath != null} video={s.VideoPath != null} cover={s.ThumbnailPath != null} charts=[{string.Join(", ", s.Charts.Select(c => $"{c.Difficulty}:{c.Level}{(c.BmsPath != null ? $"(BMS {Path.GetFileName(c.BmsPath)})" : File.Exists(c.ChartPath) ? "" : "(템플릿)")}"))}]");
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CustomSongs.Inject", required: true)]
    public static void Inject()
    {
        if (!ModHealth.CustomSongsAllowed) return;
        var mgr = SongDataManager.Instance;
        if (mgr == null) { Core.Log.Warning("[CustomSongs] SongDataManager 없음"); return; }

        var songs = mgr.SongInfoCollection;
        var patterns = mgr.PatternRawData;
        if (songs == null || patterns == null)
        {
            Core.Log.Warning($"[CustomSongs] 데이터 미로드 songs={songs?.Count} patterns={patterns?.Count}");
            return;
        }
        if (All.Count == 0) return;
        // BuildNameBanners·CreateAll마다 불린다. 같은 목록에 이미 넣었으면 패턴 전체(곡 수 × 난이도)를 다시 훑지 않는다.
        // 게임이 데이터를 새 목록으로 다시 만들거나 같은 목록을 비우고 채우면(개수 변화) 아래에서 다시 확인한다.
        if (Injected && patterns.Pointer == s_injectedList && patterns.Count == s_injectedCount) return;
        int firstIndex = All[0].Charts[0].PatternIndex;
        foreach (var p in patterns)
            if (p.PatternIndex == firstIndex) { MarkInjected(patterns.Pointer, patterns.Count); return; } // 이미 주입됨

        // 기반 패턴: 일반 기본곡(lock=none, ESD=default)의 4라인 Easy.
        // 튜토리얼(tutorial_kr, lock=unlisted)을 쓰면 BGA 영상/타임 이벤트에 튜토리얼 안내가 섞여 나온다(실측).
        PatternInfo basePattern = null, fallback = null;
        foreach (var p in patterns)
        {
            var path = p.FilePath;
            if (path == null || Path.GetFileNameWithoutExtension(path.Replace('\\', '/')) != BasePatternKey) continue;
            fallback ??= p;
            if (IsPreferredBaseSong(p))
            {
                basePattern = p;
                break;
            }
        }
        basePattern ??= fallback;
        if (basePattern is null) { Core.Log.Warning($"[CustomSongs] 기반 패턴 {BasePatternKey} 없음"); return; }

        var baseSong = basePattern.FindSongInfo();
        if (baseSong is null) { Core.Log.Warning("[CustomSongs] 기반 곡 SongInfo 없음"); return; }
        s_baseSong = baseSong;
        s_baseSongId = baseSong.Id;
        s_baseFileName = basePattern.GetPatternFileName();
        Core.Log.Msg($"[CustomSongs] 기반: song={baseSong.Id} idx={basePattern.PatternIndex} diff={basePattern.Difficulty} path={basePattern.FilePath}");

        // 난이도 문자열은 _difficultyTypeInfos에 있는 키여야 한다. 없는 키(실측: "normal")를 쓰면
        // 선곡 화면 로딩 중 "DifficultyTypeInfo is not available" 예외로 ACCESSING 화면에서 멈춘다.
        var validDifficulties = ReadValidDifficulties(mgr);
        Core.Log.Msg($"[CustomSongs] 게임 난이도 키: {string.Join(", ", validDifficulties)}");
        foreach (var custom in All)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unmatched = new List<CustomChart>();
            // 1차: 게임에 존재하는 정확한 난이도 키 먼저 배정
            foreach (var chart in custom.Charts)
            {
                var match = validDifficulties.FirstOrDefault(v => string.Equals(v, chart.Difficulty, StringComparison.OrdinalIgnoreCase));
                if (match != null && used.Add(match))
                    chart.GameDifficulty = match;
                else
                    unmatched.Add(chart);
            }
            // 2차: 없는/중복 난이도는 레벨 오름차순으로 남은 유효 키(easy → hard → over → plus → ...)에 순서대로 배정
            foreach (var chart in unmatched.OrderBy(c => c.Level))
            {
                var replacement = validDifficulties.FirstOrDefault(v => !used.Contains(v));
                Core.Log.Warning($"[CustomSongs] {custom.Id} 난이도 '{chart.Difficulty}'(Lv{chart.Level})는 게임에 없거나 중복 → '{replacement}'");
                if (replacement != null) { chart.GameDifficulty = replacement; used.Add(replacement); }
            }
        }

        // 기반 곡의 난이도별 원본 패턴을 한 번만 모아 두어 곡 수에 비례한 IL2CPP 게터 반복 호출을 없앤다.
        var basePatternsByDiff = new Dictionary<string, PatternInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in patterns)
        {
            if (p.GroupId == s_baseSongId && p.LineModeType == basePattern.LineModeType && !string.IsNullOrEmpty(p.Difficulty))
                basePatternsByDiff.TryAdd(p.Difficulty, p);
        }

        foreach (var custom in All)
        {
            // 곡 ID로 조회하는 사전(_songMetaData)에 먼저 등록해야 IsHiddenFromDefaultList 등이 예외 없이 동작한다
            RegisterSongMeta(mgr, custom, baseSong.Id);

            var song = new SongInfo(baseSong);
            SetSongField(song, "Id", custom.Id);
            SetSongField(song, "SonglistCategorizationTags", new Il2CppStringArray(new[] { CategoryTag }));
            // 실측 lockType 분포: none=180, unlisted=5(튜토리얼 등), anonymous=6, lock=4, hidden=2.
            // 보유/잠금 판정 사전은 이 시점에 아직 비어 있어 다른 곡에서 차용할 수 없으므로 일반 곡 값으로 고정.
            SetSongField(song, "LockTypeString", "none");
            songs.Add(song);

            foreach (var chart in custom.Charts)
            {
                if (chart.GameDifficulty == null) continue;
                // 같은 기반 곡에서 난이도가 일치하는 4라인 패턴을 복제해 Difficulty 문자열이 게임에서 유효하게 한다
                var source = basePatternsByDiff.GetValueOrDefault(chart.GameDifficulty, basePattern);
                var pattern = new PatternInfo(source);
                SetPatternField(pattern, "GroupId", custom.Id);
                SetPatternField(pattern, "PatternIndex", chart.PatternIndex);
                SetPatternField(pattern, "Level", chart.Level);
                SetPatternField(pattern, "PreciseLevel", (float)chart.Level);
                SetPatternField(pattern, "Patterner", custom.Patterner);
                SetPatternField(pattern, "Difficulty", chart.GameDifficulty);
                patterns.Add(pattern);
                chart.Pattern = pattern;
                Core.Log.Msg($"[CustomSongs] 주입: {custom.Id} '{custom.Title}' {chart.GameDifficulty} Lv{chart.Level} (pattern {chart.PatternIndex}, 기반 {source.FilePath}) -> {pattern.FindSongInfo()?.Id ?? "null"}");
            }
        }
        MarkInjected(patterns.Pointer, patterns.Count);
    }

    private static IntPtr s_injectedList;
    private static int s_injectedCount;

    private static string SongIdMapPath => Path.Combine(MelonEnvironment.UserDataDirectory, "PLATiNALAB", "song_ids.json");

    private static Dictionary<string, int> LoadSongIdMap()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = SongIdMapPath;
            if (!File.Exists(path)) return map;
            var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
            if (parsed != null)
                foreach (var (k, v) in parsed)
                    if (!string.IsNullOrEmpty(k) && v > 0) map[k] = v;
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[CustomSongs] song_ids.json 읽기 실패, 새로 생성합니다: {e.Message}");
        }
        return map;
    }

    private static void SaveSongIdMap(Dictionary<string, int> map)
    {
        try
        {
            var path = SongIdMapPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = System.Text.Json.JsonSerializer.Serialize(map, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[CustomSongs] song_ids.json 저장 실패: {e.Message}");
        }
    }

    private static void MarkInjected(IntPtr list, int count)
    {
        s_injectedList = list;
        s_injectedCount = count;
        Injected = true;
    }

    // 아래 세 곳은 실패해도 주입을 계속할 수 있는 선택 경로다. 게임 멤버 접근은 *Direct 메서드로 빼고 여기서 try로 감싼다.
    // 예전에는 try가 같은 메서드 안에 있어서, 멤버가 사라지면 JIT 단계 예외가 그 try를 건너뛰고 Inject 전체(= 커스텀 곡 주입)를 실패시켰다.

    private static bool IsPreferredBaseSong(PatternInfo p)
    {
        try { return IsPreferredBaseSongDirect(p); }
        catch (Exception e)
        {
            HookGuard.Fail("CustomSongs.IsPreferredBaseSong", e); // 기반 곡 선호 조건만 포기 (FilePath가 맞는 첫 패턴 사용)
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CustomSongs.IsPreferredBaseSong")]
    private static bool IsPreferredBaseSongDirect(PatternInfo p)
    {
        var s = p.FindSongInfo();
        return s is not null && s.LockTypeString == "none" && s.ESDProduct == "default";
    }

    /// <summary>실측 키(2026-10-30). _difficultyTypeInfos를 못 읽을 때 쓴다.</summary>
    private static readonly string[] DefaultDifficulties = { "easy", "hard", "over", "plus", "testpattern" };

    private static List<string> ReadValidDifficulties(SongDataManager mgr)
    {
        var valid = new List<string>();
        try { ReadDifficultyKeysDirect(mgr, valid); }
        catch (Exception e)
        {
            HookGuard.Fail("CustomSongs.ReadDifficultyKeys", e);
            valid.Clear();
            Core.Log.Warning($"[CustomSongs] _difficultyTypeInfos 읽기 실패, 기본 난이도 목록 사용: {e.Message}");
        }
        if (valid.Count == 0) valid.AddRange(DefaultDifficulties);
        return valid;
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CustomSongs.ReadDifficultyKeys")]
    private static void ReadDifficultyKeysDirect(SongDataManager mgr, List<string> into)
    {
        var infos = mgr._difficultyTypeInfos;
        if (infos == null) return;
        foreach (var kv in infos) into.Add(kv.Key);
    }

    private static void RegisterSongMeta(SongDataManager mgr, CustomSong custom, string baseSongId)
    {
        try { RegisterSongMetaDirect(mgr, custom, baseSongId); }
        catch (Exception e)
        {
            HookGuard.Fail("CustomSongs.RegisterSongMeta", e);
            Core.Log.Warning($"[CustomSongs] _songMetaData 등록 실패 ({custom.Id}): {e.Message}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CustomSongs.RegisterSongMeta")]
    private static void RegisterSongMetaDirect(SongDataManager mgr, CustomSong custom, string baseSongId)
    {
        var metas = mgr._songMetaData;
        if (metas == null || metas.ContainsKey(custom.Id)) return;
        var baseMeta = metas.ContainsKey(baseSongId) ? metas[baseSongId] : null;
        var meta = new SongMetaDataInfo();
        PopulateSongMeta(meta, baseMeta, custom);
        metas.Add(custom.Id, meta);
    }

    private static void PopulateSongMeta(SongMetaDataInfo meta, SongMetaDataInfo baseMeta, CustomSong custom)
    {
        try
        {
            PopulateSongMetaDirect(meta, baseMeta, custom);
            return;
        }
        catch { /* 리플렉션 폴백 */ }

        UpdateResilience.SafeSetFieldOrProperty(meta, "Id", custom.Id);
        UpdateResilience.SafeSetFieldOrProperty(meta, "Title", custom.Title);
        UpdateResilience.SafeSetFieldOrProperty(meta, "Artist", custom.Artist);
        UpdateResilience.SafeSetFieldOrProperty(meta, "Composer", custom.Artist);
        string baseBpm = baseMeta != null ? UpdateResilience.SafeGetFieldOrProperty<string>(baseMeta, "BPM") : null;
        UpdateResilience.SafeSetFieldOrProperty(meta, "BPM", custom.Bpm ?? baseBpm);
        if (baseMeta != null)
        {
            var genre = UpdateResilience.SafeGetFieldOrProperty<string>(baseMeta, "Genre");
            if (genre != null) UpdateResilience.SafeSetFieldOrProperty(meta, "Genre", genre);
            var licensed = UpdateResilience.SafeGetFieldOrProperty<string>(baseMeta, "LicensedBy");
            if (licensed != null) UpdateResilience.SafeSetFieldOrProperty(meta, "LicensedBy", licensed);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CustomSongs.PopulateSongMetaDirect")]
    private static void PopulateSongMetaDirect(SongMetaDataInfo meta, SongMetaDataInfo baseMeta, CustomSong custom)
    {
        if (baseMeta != null) { meta.Genre = baseMeta.Genre; meta.LicensedBy = baseMeta.LicensedBy; }
        meta.Id = custom.Id;
        meta.Title = custom.Title;
        meta.Artist = custom.Artist;
        meta.Composer = custom.Artist;
        meta.BPM = custom.Bpm ?? baseMeta?.BPM;
    }

    /// <summary>훅에서 Inject를 부를 때의 공통 처리. 바인딩 실패면 주입을 끄고, 그 밖의 예외는 전체 스택을 남긴다.</summary>
    internal static void InjectGuarded()
    {
        const string hook = "CustomSongs.Inject";
        if (HookGuard.IsDisabled(hook)) return;
        try { Inject(); }
        catch (Exception e)
        {
            if (!HookGuard.Fail(hook, e, required: true)) Core.Log.Error($"[CustomSongs] 주입 실패: {e}");
        }
    }

    /// <summary>
    /// SongInfo 멤버를 이름으로 찾아 설정하고, 실패하면 직접 필드 대입으로 넘어간다.
    /// 직접 대입은 별도 메서드로 분리한다. 같은 메서드에 두면 그 필드가 사라질 때 JIT 단계에서 이름 기반 경로까지 함께 실패한다.
    /// </summary>
    private static void SetSongField(SongInfo song, string propName, object value)
    {
        if (UpdateResilience.SafeSetFieldOrProperty(song, propName, value)) return;
        try { SetSongFieldDirect(song, propName, value); }
        catch (Exception e) { Core.Log.Warning($"[CustomSongs] SongInfo.{propName} 설정 폴백 실패: {e.Message}"); }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CustomSongs.SetSongFieldDirect")]
    private static void SetSongFieldDirect(SongInfo song, string propName, object value)
    {
        switch (propName)
        {
            case "Id": song._Id_k__BackingField = (string)value; break;
            case "SonglistCategorizationTags": song._SonglistCategorizationTags_k__BackingField = (Il2CppStringArray)value; break;
            case "LockTypeString": song._LockTypeString_k__BackingField = (string)value; break;
        }
    }

    /// <summary>
    /// PatternInfo 멤버를 이름으로 찾아 설정하고, 실패하면 직접 필드 대입으로 넘어간다 (분리 이유는 SetSongField와 같다).
    /// </summary>
    private static void SetPatternField(PatternInfo pattern, string propName, object value)
    {
        if (UpdateResilience.SafeSetFieldOrProperty(pattern, propName, value)) return;
        try { SetPatternFieldDirect(pattern, propName, value); }
        catch (Exception e) { Core.Log.Warning($"[CustomSongs] PatternInfo.{propName} 설정 폴백 실패: {e.Message}"); }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CustomSongs.SetPatternFieldDirect")]
    private static void SetPatternFieldDirect(PatternInfo pattern, string propName, object value)
    {
        switch (propName)
        {
            case "GroupId": pattern._GroupId_k__BackingField = (string)value; break;
            case "PatternIndex": pattern._PatternIndex_k__BackingField = (int)value; break;
            case "Level": pattern._Level_k__BackingField = (int)value; break;
            case "PreciseLevel": pattern._PreciseLevel_k__BackingField = Convert.ToSingle(value); break;
            case "Patterner": pattern._Patterner_k__BackingField = (string)value; break;
            case "Difficulty": pattern._Difficulty_k__BackingField = (string)value; break;
        }
    }

    /// <summary>기반 차트에서 가장 앞의 노트 하나만 남긴 PPP JSON (곡 폴더에 chart.json이 없을 때 템플릿).</summary>
    public static byte[] MakeFirstNoteChart(byte[] original)
    {
        var root = JsonNode.Parse(original)!.AsObject();
        var notes = root["notes"]!.AsArray();
        JsonNode first = null;
        foreach (var n in notes)
            if (first == null || n!["startPosition"]!.GetValue<double>() < first["startPosition"]!.GetValue<double>())
                first = n;
        var kept = new JsonArray();
        if (first != null) kept.Add(JsonNode.Parse(first.ToJsonString()));
        root["notes"] = kept;
        // 기반 곡의 HideUI/ShowUI 같은 연출 이벤트는 커스텀 곡에 맞지 않으므로 제거
        root["timeTriggeredEvents"] = new JsonArray();
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(root);
    }

    /// <summary>
    /// 커스텀 차트의 bgms[].sound를 기반 차트의 BGM 파일명으로 바꾼다.
    /// 게임은 Data/Patterns/{기반곡}/{sound}를 Addressables로 로드하므로 존재하는 이름이어야 로딩이 통과한다.
    /// 실제 재생 음원은 SoundData.FromHEAudioClip 패치에서 music.ogg로 교체된다.
    /// </summary>
    public static byte[] PrepareChart(byte[] custom, byte[] baseChart, CustomSong song)
    {
        try
        {
            var root = JsonNode.Parse(custom)!.AsObject();
            var baseRoot = baseChart != null ? JsonNode.Parse(baseChart) as JsonObject : null;
            return PrepareChart(root, baseRoot, song);
        }
        catch (Exception e)
        {
            Core.Log.Error($"[CustomSongs] PrepareChart JSON 파싱 예외 발생, 원본 차트 폴백: {e.Message}");
            return custom ?? baseChart ?? Array.Empty<byte>();
        }
    }

    /// <summary>이미 파싱된 JsonObject를 재직렬화/재파싱 없이 마무리하고 UTF-8 바이트로 1회 직렬화한다.</summary>
    public static byte[] PrepareChart(JsonObject root, JsonObject baseRoot, CustomSong song)
    {
        try
        {
            var baseSound = baseRoot?["bgms"]?.AsArray().FirstOrDefault()?["sound"]?.GetValue<string>();
            var bgms = root["bgms"] as JsonArray;
            if (bgms == null || bgms.Count == 0)
            {
                bgms = new JsonArray { new JsonObject { ["position"] = 1, ["sound"] = baseSound, ["column"] = 0 } };
                root["bgms"] = bgms;
            }
            if (baseSound != null)
                foreach (var b in bgms) b!["sound"] = baseSound;

            ExtendToMediaLength(root, song);
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(root);
        }
        catch (Exception e)
        {
            Core.Log.Error($"[CustomSongs] PrepareChart 처리 예외 발생: {e.Message}");
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(root);
        }
    }

    /// <summary>
    /// 게임은 endPosition(마디)에서 곡을 끝내고 결과 화면으로 간다. 차트(특히 기반 곡 템플릿)의 endPosition이
    /// music.ogg / video.mp4보다 짧으면 BGM·BGA 중간에 끝나버리므로(실측), 둘 중 긴 길이 + 1초까지 늘린다.
    /// 위치 = BGM 시작 위치 + 초 × BPM / 240 (4/4, 고정 BPM 기준).
    /// </summary>
    private static void ExtendToMediaLength(JsonObject root, CustomSong song)
    {
        double? music = song.MusicPath != null ? MediaDuration.Ogg(song.MusicPath) : null;
        double? video = song.VideoPath != null ? MediaDuration.Mp4(song.VideoPath) : null;
        double seconds = Math.Max(music ?? 0, video ?? 0);
        if (seconds <= 0) return;

        double bpm = root["initialBpm"]?.GetValue<double>() ?? 120;
        double bgmPos = root["bgms"]?.AsArray().FirstOrDefault()?["position"]?.GetValue<double>() ?? 1;
        if (root["bpms"] is JsonObject bpms && bpms.Count > 0)
            Core.Log.Warning($"[CustomSongs] {song.Id} 차트에 BPM 변속이 있어 길이 계산이 근사값입니다 (initialBpm {bpm} 기준)");
        double needed = Math.Ceiling(bgmPos + (seconds + 1.0) * bpm / 240.0);
        double current = root["endPosition"]?.GetValue<double>() ?? 0;
        if (needed <= current) return;
        root["endPosition"] = needed;
        Core.Log.Msg($"[CustomSongs] {song.Id} endPosition {current} → {needed} (music {music:F1}s, video {video:F1}s, bpm {bpm})");
    }
}
