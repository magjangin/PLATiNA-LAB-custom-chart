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
    public readonly List<CustomChart> Charts = new();

    public string MusicPath => FirstExisting("music.ogg");
    public string VideoPath => FirstExisting("video.mp4");
    public string ThumbnailPath => FirstExisting("썸네일.png", "thumbnail.png", "cover.png", "jacket.png");

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

        var info = Path.Combine(folder, "info.txt");
        if (File.Exists(info))
        {
            foreach (var raw in File.ReadAllLines(info))
            {
                var line = raw.Trim().TrimStart('﻿');
                int sep = line.IndexOfAny(new[] { ':', '=' });
                if (line.StartsWith("#") || sep <= 0) continue;
                var key = line[..sep].Trim().ToLowerInvariant().Replace(" ", "");
                var value = line[(sep + 1)..].Trim();
                switch (key)
                {
                    case "곡제목": case "제목": case "title": song.Title = value; break;
                    case "아티스트": case "artist": song.Artist = value; break;
                    case "bpm": song.Bpm = value; break;
                    case "패턴제작자": case "채보": case "patterner": song.Patterner = value; break;
                    case "난이도": case "difficulty": case "difficulties":
                        difficulties.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(d => d.ToLowerInvariant()));
                        break;
                    default:
                        if (int.TryParse(value, out var lv)) levels[key] = lv;
                        break;
                }
            }
        }
        // BPM이 info에 없으면 같은 폴더 BMS의 #BPM
        if (song.Bpm == null)
        {
            var bms = Directory.GetFiles(folder, "*.bms").FirstOrDefault();
            var bpmLine = bms == null ? null : File.ReadLines(bms).FirstOrDefault(l => l.StartsWith("#BPM ", StringComparison.OrdinalIgnoreCase));
            if (bpmLine != null) song.Bpm = bpmLine[5..].Trim();
        }

        if (difficulties.Count == 0) difficulties.Add("easy");
        for (int i = 0; i < difficulties.Count && i < 10; i++)
        {
            var d = difficulties[i];
            song.Charts.Add(new CustomChart
            {
                Song = song,
                Difficulty = d,
                Level = levels.TryGetValue(d, out var lv) ? lv : levels.GetValueOrDefault("level", 1),
                PatternIndex = CustomSongs.PatternIndexBase + number * 10 + i,
            });
        }
        return song;
    }
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

    /// <summary>chart_{난이도}.json 우선, 없으면 chart.json. 둘 다 없으면 chart_{난이도}.json으로 템플릿을 만든다.</summary>
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

    public static bool IsCustomId(string songId) => songId != null && songId.StartsWith(IdPrefix);
    public static CustomSong ById(string songId) => All.FirstOrDefault(s => s.Id == songId);
    public static CustomChart ByPatternIndex(int index)
    {
        foreach (var s in All) foreach (var c in s.Charts) if (c.PatternIndex == index) return c;
        return null;
    }

    public static CustomChart ByKey(string key)
    {
        foreach (var s in All) foreach (var c in s.Charts) if (c.Key == key) return c;
        return null;
    }
    public static CustomChart Of(PatternInfo info) => info == null ? null : ByPatternIndex(info.PatternIndex);
    public static bool IsCustom(PatternInfo info) => Of(info) != null;

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
        for (int i = 0; i < folders.Count; i++) All.Add(CustomSong.Load(folders[i], i + 1));
        foreach (var s in All)
            Core.Log.Msg($"[CustomSongs] {s.Id} '{s.Title}' ({Path.GetFileName(s.Folder)}) music={s.MusicPath != null} video={s.VideoPath != null} cover={s.ThumbnailPath != null} charts=[{string.Join(", ", s.Charts.Select(c => $"{c.Difficulty}:{c.Level}{(File.Exists(c.ChartPath) ? "" : "(템플릿)")}"))}]");
    }

    public static void Inject()
    {
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
        foreach (var p in patterns)
            if (p.PatternIndex == All[0].Charts[0].PatternIndex) return; // 이미 주입됨

        // 기반 패턴: 일반 기본곡(lock=none, ESD=default)의 4라인 Easy.
        // 튜토리얼(tutorial_kr, lock=unlisted)을 쓰면 BGA 영상/타임 이벤트에 튜토리얼 안내가 섞여 나온다(실측).
        PatternInfo basePattern = null, fallback = null;
        foreach (var p in patterns)
        {
            var path = p.FilePath;
            if (path == null || Path.GetFileNameWithoutExtension(path.Replace('\\', '/')) != BasePatternKey) continue;
            fallback ??= p;
            var s = p.FindSongInfo();
            if (s != null && s.LockTypeString == "none" && s.ESDProduct == "default")
            {
                basePattern = p;
                break;
            }
        }
        basePattern ??= fallback;
        if (basePattern == null) { Core.Log.Warning($"[CustomSongs] 기반 패턴 {BasePatternKey} 없음"); return; }

        var baseSong = basePattern.FindSongInfo();
        if (baseSong == null) { Core.Log.Warning("[CustomSongs] 기반 곡 SongInfo 없음"); return; }
        s_baseSong = baseSong;
        s_baseFileName = basePattern.GetPatternFileName();
        Core.Log.Msg($"[CustomSongs] 기반: song={baseSong.Id} idx={basePattern.PatternIndex} diff={basePattern.Difficulty} path={basePattern.FilePath}");

        // 난이도 문자열은 _difficultyTypeInfos에 있는 키여야 한다. 없는 키(실측: "normal")를 쓰면
        // 선곡 화면 로딩 중 "DifficultyTypeInfo is not available" 예외로 ACCESSING 화면에서 멈춘다.
        var validDifficulties = new List<string>();
        if (mgr._difficultyTypeInfos != null)
            foreach (var kv in mgr._difficultyTypeInfos) validDifficulties.Add(kv.Key);
        Core.Log.Msg($"[CustomSongs] 게임 난이도 키: {string.Join(", ", validDifficulties)}");
        foreach (var custom in All)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var chart in custom.Charts)
            {
                var match = validDifficulties.FirstOrDefault(v => string.Equals(v, chart.Difficulty, StringComparison.OrdinalIgnoreCase));
                if (match == null || used.Contains(match))
                {
                    // 없는 이름은 아직 안 쓴 유효 키 중 첫 번째로 대체
                    var replacement = validDifficulties.FirstOrDefault(v => !used.Contains(v) && !custom.Charts.Any(c => string.Equals(c.Difficulty, v, StringComparison.OrdinalIgnoreCase)));
                    Core.Log.Warning($"[CustomSongs] {custom.Id} 난이도 '{chart.Difficulty}'는 게임에 없음 → '{replacement}'");
                    match = replacement;
                }
                if (match != null) { chart.GameDifficulty = match; used.Add(match); }
            }
        }

        var metas = mgr._songMetaData;
        SongMetaDataInfo baseMeta = metas != null && metas.ContainsKey(baseSong.Id) ? metas[baseSong.Id] : null;

        foreach (var custom in All)
        {
            // 곡 ID로 조회하는 사전(_songMetaData)에 먼저 등록해야 IsHiddenFromDefaultList 등이 예외 없이 동작한다
            if (metas != null && !metas.ContainsKey(custom.Id))
            {
                var meta = new SongMetaDataInfo();
                if (baseMeta != null) { meta.Genre = baseMeta.Genre; meta.LicensedBy = baseMeta.LicensedBy; }
                meta.Id = custom.Id;
                meta.Title = custom.Title;
                meta.Artist = custom.Artist;
                meta.Composer = custom.Artist;
                meta.BPM = custom.Bpm ?? baseMeta?.BPM;
                metas.Add(custom.Id, meta);
            }

            var song = new SongInfo(baseSong);
            song._Id_k__BackingField = custom.Id;
            song._SonglistCategorizationTags_k__BackingField = new Il2CppStringArray(new[] { CategoryTag });
            // 실측 lockType 분포: none=180, unlisted=5(튜토리얼 등), anonymous=6, lock=4, hidden=2.
            // 보유/잠금 판정 사전은 이 시점에 아직 비어 있어 다른 곡에서 차용할 수 없으므로 일반 곡 값으로 고정.
            song._LockTypeString_k__BackingField = "none";
            songs.Add(song);

            foreach (var chart in custom.Charts)
            {
                if (chart.GameDifficulty == null) continue;
                // 같은 기반 곡에서 난이도가 일치하는 4라인 패턴을 복제해 Difficulty 문자열이 게임에서 유효하게 한다
                PatternInfo source = basePattern;
                foreach (var p in patterns)
                    if (p.GroupId == baseSong.Id && p.LineModeType == basePattern.LineModeType &&
                        string.Equals(p.Difficulty, chart.GameDifficulty, StringComparison.OrdinalIgnoreCase)) { source = p; break; }
                var pattern = new PatternInfo(source);
                pattern._GroupId_k__BackingField = custom.Id;
                pattern._PatternIndex_k__BackingField = chart.PatternIndex;
                pattern._Level_k__BackingField = chart.Level;
                pattern._PreciseLevel_k__BackingField = chart.Level;
                pattern._Patterner_k__BackingField = custom.Patterner;
                pattern._Difficulty_k__BackingField = chart.GameDifficulty;
                patterns.Add(pattern);
                chart.Pattern = pattern;
                Core.Log.Msg($"[CustomSongs] 주입: {custom.Id} '{custom.Title}' {pattern.Difficulty} Lv{chart.Level} (pattern {chart.PatternIndex}, 기반 {source.FilePath}) -> {pattern.FindSongInfo()?.Id ?? "null"}");
            }
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
        return System.Text.Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    /// <summary>
    /// 커스텀 차트의 bgms[].sound를 기반 차트의 BGM 파일명으로 바꾼다.
    /// 게임은 Data/Patterns/{기반곡}/{sound}를 Addressables로 로드하므로 존재하는 이름이어야 로딩이 통과한다.
    /// 실제 재생 음원은 SoundData.FromHEAudioClip 패치에서 music.ogg로 교체된다.
    /// </summary>
    public static byte[] PrepareChart(byte[] custom, byte[] baseChart, CustomSong song)
    {
        var root = JsonNode.Parse(custom)!.AsObject();
        var baseSound = JsonNode.Parse(baseChart)?["bgms"]?.AsArray().FirstOrDefault()?["sound"]?.GetValue<string>();
        var bgms = root["bgms"] as JsonArray;
        if (bgms == null || bgms.Count == 0)
        {
            bgms = new JsonArray { new JsonObject { ["position"] = 1, ["sound"] = baseSound, ["column"] = 0 } };
            root["bgms"] = bgms;
        }
        if (baseSound != null)
            foreach (var b in bgms) b!["sound"] = baseSound;

        ExtendToMediaLength(root, song);
        return System.Text.Encoding.UTF8.GetBytes(root.ToJsonString());
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

[HarmonyPatch(typeof(SongListBuilder), nameof(SongListBuilder.Build))]
internal static class SongListBuilder_Build_Patch
{
    private static void Prefix()
    {
        Core.Log.Msg("[CustomSongs] SongListBuilder.Build");
        try { CustomSongs.Inject(); }
        catch (Exception e) { Core.Log.Error($"[CustomSongs] 주입 실패: {e}"); }
    }
}

[HarmonyPatch(typeof(SongListBuilder), nameof(SongListBuilder.BuildNameBanners))]
internal static class SongListBuilder_BuildNameBanners_Patch
{
    private static void Prefix()
    {
        Core.Log.Msg("[CustomSongs] SongListBuilder.BuildNameBanners");
        try { CustomSongs.Inject(); }
        catch (Exception e) { Core.Log.Error($"[CustomSongs] 주입 실패: {e}"); }
    }
}

// 커스텀 곡은 게임 카탈로그/보유 정보 사전에 없어서 원본 판정이 KeyNotFound를 던진다 → 직접 고정값을 돌려준다
[HarmonyPatch(typeof(SongInfo), nameof(SongInfo.IsHiddenFromDefaultList), MethodType.Getter)]
internal static class SongInfo_IsHidden_Patch
{
    private static bool Prefix(SongInfo __instance, ref bool __result)
    {
        if (!CustomSongs.IsCustomId(__instance.Id)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(PatternInfo), nameof(PatternInfo.GetPatternFileName))]
internal static class PatternInfo_GetPatternFileName_Patch
{
    private static void Postfix(PatternInfo __instance, ref string __result)
    {
        if (!CustomSongs.IsCustom(__instance) || CustomSongs.s_baseFileName == null) return;
        if (__result != CustomSongs.s_baseFileName)
            Core.Log.Msg($"[CustomSongs] GetPatternFileName '{__result}' -> '{CustomSongs.s_baseFileName}'");
        __result = CustomSongs.s_baseFileName;
    }
}

[HarmonyPatch(typeof(SongInfo), nameof(SongInfo.IsAvailable))]
internal static class SongInfo_IsAvailable_Patch
{
    private static bool Prefix(SongInfo __instance, ref bool __result)
    {
        if (!CustomSongs.IsCustomId(__instance.Id)) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(SongInfo), nameof(SongInfo.IsNotOwned))]
internal static class SongInfo_IsNotOwned_Patch
{
    private static bool Prefix(SongInfo __instance, ref bool __result)
    {
        if (!CustomSongs.IsCustomId(__instance.Id)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(SongInfo), nameof(SongInfo.IsLock))]
internal static class SongInfo_IsLock_Patch
{
    private static bool Prefix(SongInfo __instance, ref Il2CppPlatinaLab.Common.EncryptedPrimitives.EncryptedBool __result)
    {
        if (!CustomSongs.IsCustomId(__instance.Id)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(SongInfo), nameof(SongInfo.IsNewSongMarkTarget))]
internal static class SongInfo_IsNewSongMarkTarget_Patch
{
    private static bool Prefix(SongInfo __instance, ref Il2CppPlatinaLab.Common.EncryptedPrimitives.EncryptedBool __result)
    {
        if (!CustomSongs.IsCustomId(__instance.Id)) return true;
        __result = false;
        return false;
    }
}

/// <summary>
/// 카테고리 UI(탭 버튼, 선곡 화면 상단 라벨 등)는 SongCategoryResourceCollection.GetDataSet(key)의 리소스 세트로 그려진다.
/// custom은 세트가 없어 상단 라벨이 흰 판(스프라이트 없는 Image)으로 나왔다(실측).
/// ALL 카테고리 세트에서 배경/색을 빌리고 이름 칸들은 RenderType.Text + DisplayedName "CUSTOM"으로 만든 세트를 돌려줘
/// 게임 자체 경로로 다른 카테고리와 같은 모양으로 그리게 한다.
/// </summary>
[HarmonyPatch(typeof(SongCategoryResourceCollection), nameof(SongCategoryResourceCollection.GetDataSet))]
internal static class SongCategoryResourceCollection_GetDataSet_Patch
{
    private const string DonorKey = "all";
    private const string DisplayedName = "CUSTOM";
    private static SongCategoryResourceDataSet s_dataSet;

    private static void Postfix(SongCategoryResourceCollection __instance, string categoryKey, ref SongCategoryResourceDataSet __result)
    {
        if (categoryKey != CustomSongs.CategoryTag) return;
        try
        {
            if (s_dataSet == null || s_dataSet.WasCollected) s_dataSet = Create(__instance);
            if (s_dataSet != null) __result = s_dataSet;
        }
        catch (Exception e) { Core.Log.Error($"[CustomSongs] custom 카테고리 리소스 생성 실패: {e}"); }
    }

    private static SongCategoryResourceDataSet Create(SongCategoryResourceCollection collection)
    {
        var donor = collection.GetDataSet(DonorKey);
        if (donor == null && collection._categoryResourceDataSet?.Length > 0) donor = collection._categoryResourceDataSet[0];

        var ds = UnityEngine.ScriptableObject.CreateInstance(Il2CppType.Of<SongCategoryResourceDataSet>()).Cast<SongCategoryResourceDataSet>();
        ds.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
        ds._categoryKey = CustomSongs.CategoryTag;
        ds._displayedName = DisplayedName;
        if (donor != null)
        {
            ds._carouselBackground = donor._carouselBackground;
            ds._detailFocusBackground = donor._detailFocusBackground;
            ds._categoryColor = donor._categoryColor;
        }
        ds._songBanner = TextResource();
        ds._smallLabel = TextResource();
        ds._booth = TextResource();
        ds._eyeCatchV1 = TextResource();
        ds._detailBanner = TextResource();
        Core.Log.Msg($"[CustomSongs] custom 카테고리 리소스 세트 생성 (배경/색 차용: {donor?._categoryKey ?? "없음"})");
        return ds;
    }

    private static CategoryResource TextResource()
    {
        var r = new CategoryResource();
        r._resourceRenderType = CategoryResource.RenderType.Text;
        return r;
    }
}

/// <summary>
/// 게임은 모든 노트가 처리되면 PatternPlayer.DetermineRunning()에서 _isLastNoteProcessingTriggered(+0x60)를 세우고
/// EventQueue에 클리어 이벤트(3=PERFECT / 4=FULL COMBO / 5=CLEAR, GameAssembly 0x2CF5381~0x2CF53AC 실측)를 넣어
/// 곧바로 클리어 연출(PERFECT DECODE)로 넘어간다. 1노트 커스텀 차트는 첫 노트 직후에 끝나버리므로,
/// DetermineRunning 동안만 플래그를 true로 세워 그 블록을 건너뛰게 하고, 곡 끝(차트 길이 - 여유)에 도달하면 막지 않는다.
/// DetermineRunning은 입력 처리 스레드에서도 불리므로 Update에 걸면 막히지 않는다(실측).
/// </summary>
[HarmonyPatch(typeof(Il2CppPlatinaLab.Main.InGame.PatternPlayer), nameof(Il2CppPlatinaLab.Main.InGame.PatternPlayer.DetermineRunning))]
internal static class PatternPlayer_DetermineRunning_HoldEnd_Patch
{
    private const int EndMarginMicroSec = 3_000_000;
    private static bool s_logged;

    private static void Prefix(Il2CppPlatinaLab.Main.InGame.PatternPlayer __instance, out bool __state)
    {
        __state = false;
        if (!CustomSongs.IsCustom(__instance.PlayPattern) || __instance._isLastNoteProcessingTriggered) return;
        var data = __instance._patternData;
        if (data == null) return;
        int endAt = data.DurationMicroSec - EndMarginMicroSec;
        if (!s_logged)
        {
            s_logged = true;
            Core.Log.Msg($"[CustomSongs] 클리어 연출 보류: {endAt / 1_000_000.0:F1}s까지 (차트 길이 {data.DurationMicroSec / 1_000_000.0:F1}s)");
        }
        if (__instance.CurrentTimeMicroseconds >= endAt) return;
        __instance._isLastNoteProcessingTriggered = true;
        __state = true;
    }

    private static void Postfix(Il2CppPlatinaLab.Main.InGame.PatternPlayer __instance, bool __state)
    {
        if (__state) __instance._isLastNoteProcessingTriggered = false;
    }
}

[HarmonyPatch(typeof(AbstractPreFilteredCache), nameof(AbstractPreFilteredCache.UpdatePostProcessData))]
internal static class AbstractPreFilteredCache_UpdatePostProcessData_Patch
{
    private static void Postfix(AbstractPreFilteredCache __instance)
    {
        if (__instance.Key != CustomSongs.CategoryTag) return;
        Core.Log.Msg($"[CustomSongs] custom 카테고리 raw={__instance.RawData?.Count} post={__instance.PostProcessData?.Count}");
    }
}

[HarmonyPatch(typeof(CategoryCacheFactory), nameof(CategoryCacheFactory.CreateAll))]
internal static class CategoryCacheFactory_CreateAll_Patch
{
    private static void Postfix(Il2CppSystem.Collections.Generic.List<AbstractPreFilteredCache> __result)
    {
        try
        {
            if (__result == null) return;
            CustomSongs.Inject();
            foreach (var c in __result)
                if (c.Key == CustomSongs.CategoryTag) return;
            var cache = new TagCategoryCache(CustomSongs.CategoryTag, CustomSongs.CategoryTag);
            __result.Add(cache);
            Core.Log.Msg($"[CustomSongs] 카테고리 '{CustomSongs.CategoryTag}' 추가 (총 {__result.Count}개)");
            var keys = new List<string>();
            foreach (var c in __result) keys.Add($"{c.Key}({c.GetType().Name}:{c.SongCategoryKey})");
            Core.Log.Msg($"[CustomSongs] 카테고리 목록: {string.Join(", ", keys)}");
        }
        catch (Exception e) { Core.Log.Error($"[CustomSongs] 카테고리 추가 실패: {e}"); }
    }
}
