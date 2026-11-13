using HarmonyLib;
using Il2CppHighendGames.HEAudio;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPlatinaLab.Main.Catalog;
using Il2CppPlatinaLab.Main.InGame;
using Il2CppPlatinaLab.Main.Select.Preview.Controller;
using Il2CppPlatinaLab.Main.UI.InGame;
using UnityEngine;

namespace PLATiNALAB.Mod;

/// <summary>
/// hwa\ 곡 폴더의 파일을 게임 리소스 대신 쓰게 한다.
/// - 썸네일.png → CoverManager.GetCoverSprite
/// - music.ogg  → 미리듣기 + 인게임 BGM (둘 다 SoundData.FromHEAudioClip에서 교체)
/// - video.mp4  → BGAPlayer.SetClip 직후 VideoPlayer를 URL 모드로 전환
/// 게임 음원(.fmod.ogg)은 HEAudioClip(ScriptableObject)에 ogg/wav 바이트를 담은 것이고
/// SoundData.FromHEAudioClip(clip, isLooping, afterDispose)로 재생 객체를 만든다(HighendGames.HEAudio 인터롭 실측).
/// SoundData 생성자와 PatternPlayer.SetMainTrackToPlay 패치는 발동하지 않았다(AGENTS.md Dead Ends).
/// </summary>
internal static class CustomMedia
{
    private const int MaxClipCache = 8;
    private sealed class ClipEntry
    {
        public long Ticks;
        public HEAudioClip Clip;
    }
    private static readonly Dictionary<string, ClipEntry> s_clips = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> s_clipLru = new();
    private static readonly object s_clipLock = new();

    private const int MaxCoverCache = 24;
    private sealed class CoverEntry
    {
        public long Ticks;
        public Sprite Sprite;
    }
    private static readonly Dictionary<string, CoverEntry> s_covers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> s_coverLru = new();
    private static readonly Dictionary<string, long> s_failedCovers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object s_coverLock = new();

    private static volatile bool s_standardLoadImageBroken;
    private static volatile bool s_standardSetUrlBroken;

    private static long GetWriteTicks(string path)
    {
        try { return File.GetLastWriteTimeUtc(path).Ticks; }
        catch { return 0; }
    }

    private static void TouchLru(LinkedList<string> lru, string path)
    {
        lru.Remove(path);
        lru.AddLast(path);
    }

    private static void DestroyClip(HEAudioClip clip)
    {
        if (clip == null || clip.WasCollected) return;
        try { UnityEngine.Object.Destroy(clip); } catch { }
    }

    private static void DestroySprite(Sprite sprite)
    {
        if (sprite == null || sprite.WasCollected) return;
        try
        {
            if (sprite.texture != null && !sprite.texture.WasCollected)
                UnityEngine.Object.Destroy(sprite.texture);
            UnityEngine.Object.Destroy(sprite);
        }
        catch { }
    }

    public static HEAudioClip GetClip(string path)
    {
        long ticks = GetWriteTicks(path);
        lock (s_clipLock)
        {
            if (s_clips.TryGetValue(path, out var cached) && cached.Ticks == ticks && cached.Clip != null && !cached.Clip.WasCollected)
            {
                TouchLru(s_clipLru, path);
                return cached.Clip;
            }
            if (s_clips.Remove(path, out var stale))
            {
                s_clipLru.Remove(path);
                DestroyClip(stale.Clip);
            }

            EvictClipsIfFull();
        }

        var bytes = File.ReadAllBytes(path);
        var clip = CreateAudioClip(bytes);
        if (clip == null) return null;

        lock (s_clipLock)
        {
            if (s_clips.TryGetValue(path, out var race) && race.Ticks == ticks && race.Clip != null && !race.Clip.WasCollected)
            {
                DestroyClip(clip);
                TouchLru(s_clipLru, path);
                return race.Clip;
            }
            if (s_clips.Remove(path, out var old))
            {
                s_clipLru.Remove(path);
                DestroyClip(old.Clip);
            }
            EvictClipsIfFull();
            s_clips[path] = new ClipEntry { Ticks = ticks, Clip = clip };
            s_clipLru.AddLast(path);
        }
        return clip;
    }

    private static void EvictClipsIfFull()
    {
        if (s_clips.Count < MaxClipCache) return;
        string currentMusic = CustomSongs.Current?.Song.MusicPath;
        string previewMusic = PreviewSong?.MusicPath;
        var node = s_clipLru.First;
        while (s_clips.Count >= MaxClipCache && node != null)
        {
            var next = node.Next;
            var candidate = node.Value;
            if (!string.Equals(candidate, currentMusic, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate, previewMusic, StringComparison.OrdinalIgnoreCase))
            {
                s_clipLru.Remove(node);
                if (s_clips.Remove(candidate, out var evicted))
                    DestroyClip(evicted.Clip);
            }
            node = next;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CustomMedia.CreateAudioClip")]
    private static HEAudioClip CreateAudioClip(byte[] bytes)
    {
        var clip = ScriptableObject.CreateInstance(Il2CppType.Of<HEAudioClip>()).Cast<HEAudioClip>();
        clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
        // RIFF 헤더면 wav, 아니면 ogg
        var format = bytes.Length > 4 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            ? AudioFormat.Wav : AudioFormat.Ogg;
        SetClipData(clip, bytes, format);
        return clip;
    }

    private static void SetClipData(HEAudioClip clip, byte[] bytes, AudioFormat format)
    {
        var il2cppBytes = new Il2CppStructArray<byte>(bytes);
        try
        {
            SetClipDataDirect(clip, il2cppBytes, format);
            return;
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[CustomMedia] HEAudioClip 직접 설정 실패, 리플렉션 폴백: {e.Message}");
        }
        bool okBytes = UpdateResilience.SafeSetFieldOrProperty(clip, "bytes", il2cppBytes);
        bool okFormat = UpdateResilience.SafeSetFieldOrProperty(clip, "audioFormat", format);
        if (!okBytes || !okFormat)
            Core.Log.Warning($"[CustomMedia] HEAudioClip 리플렉션 설정 불완전 (bytes={okBytes}, audioFormat={okFormat})");
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CustomMedia.SetClipDataDirect")]
    private static void SetClipDataDirect(HEAudioClip clip, Il2CppStructArray<byte> bytes, AudioFormat format)
    {
        clip._bytes = bytes;
        clip._audioFormat = format;
    }

    /// <summary>
    /// 이미지 로드 이중 래퍼 (Double-Wrapper):
    /// 1차: Unity 공식 ImageConversion.LoadImage 시도 (MelonLoader/Unity 인터롭 정상화 시 동작)
    /// 2차: MissingMethodException(ReadOnlySpan 버그) 시 네이티브 icall(LoadImage_Injected) 직접 호출
    /// 첫 호출에서 ReadOnlySpan 누락을 감지하면 이후 호출은 예외 없이 바로 icall을 쓴다.
    /// </summary>
    private static bool LoadImage(Texture2D tex, byte[] bytes)
    {
        if (tex == null || tex.WasCollected || bytes == null || bytes.Length == 0) return false;

        // 1차: 표준 공식 API 시도 (ReadOnlySpan 누락이 확인된 뒤엔 건너뛴다)
        if (!s_standardLoadImageBroken)
        {
            try
            {
                if (LoadImageStandard(tex, bytes)) return true;
            }
            catch (MissingMethodException)
            {
                s_standardLoadImageBroken = true;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[CustomMedia] 표준 LoadImage 예외 발생, icall 폴백 시도: {e.Message}");
            }
        }

        // 2차: 네이티브 icall 직접 호출
        const string icallHook = "CustomMedia.LoadImageIcall";
        if (HookGuard.IsDisabled(icallHook)) return false;
        try
        {
            return LoadImageIcall(tex, bytes);
        }
        catch (Exception e)
        {
            if (!HookGuard.Fail(icallHook, e))
                Core.Log.Error($"[CustomMedia] LoadImage icall 폴백 실패: {e.Message}");
            return false;
        }
    }

    // 두 경로를 별도 메서드로 나눈다. 한 메서드에 두면 어느 한쪽 심볼이 사라질 때 JIT 단계에서 양쪽 다 실패한다.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool LoadImageStandard(Texture2D tex, byte[] bytes) => ImageConversion.LoadImage(tex, bytes);

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CustomMedia.LoadImageIcall")]
    private static unsafe bool LoadImageIcall(Texture2D tex, byte[] bytes)
    {
        var texPtr = UnityEngine.Object.MarshalledUnityObject.MarshalNotNull(tex);
        fixed (byte* begin = bytes)
        {
            var span = new UnityEngine.Bindings.ManagedSpanWrapper(begin, bytes.Length);
            return ImageConversion.LoadImage_Injected(texPtr, ref span, false);
        }
    }

    /// <summary>
    /// VideoPlayer.url 설정 이중 래퍼:
    /// 1차: 표준 player.url 세터 시도
    /// 2차: ReadOnlySpan 버그로 실패 시 set_url_Injected icall 직접 호출
    /// </summary>
    public static void SetUrl(UnityEngine.Video.VideoPlayer player, string url)
    {
        if (player == null || player.WasCollected || string.IsNullOrEmpty(url)) return;

        // 1차: 표준 프로퍼티 시도
        if (!s_standardSetUrlBroken)
        {
            try
            {
                SetUrlStandard(player, url);
                return;
            }
            catch (MissingMethodException)
            {
                s_standardSetUrlBroken = true;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[CustomMedia] 표준 VideoPlayer.url 예외 발생, icall 폴백 시도: {e.Message}");
            }
        }

        // 2차: 네이티브 icall 직접 호출
        const string icallHook = "CustomMedia.SetUrlIcall";
        if (HookGuard.IsDisabled(icallHook)) return;
        try
        {
            SetUrlIcall(player, url);
        }
        catch (Exception e)
        {
            if (!HookGuard.Fail(icallHook, e))
                Core.Log.Error($"[CustomMedia] VideoPlayer.url icall 폴백 실패: {e.Message}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetUrlStandard(UnityEngine.Video.VideoPlayer player, string url) => player.url = url;

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CustomMedia.SetUrlIcall")]
    private static unsafe void SetUrlIcall(UnityEngine.Video.VideoPlayer player, string url)
    {
        var self = UnityEngine.Object.MarshalledUnityObject.MarshalNotNull(player);
        fixed (char* begin = url)
        {
            var span = new UnityEngine.Bindings.ManagedSpanWrapper(begin, url.Length);
            UnityEngine.Video.VideoPlayer.set_url_Injected(self, ref span);
        }
    }

    /// <summary>미리듣기 중인 커스텀 곡 (PreviewSoundController.Play에서 설정).</summary>
    public static CustomSong PreviewSong;

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("CoverManager.GetCoverSprite")]
    public static Sprite GetCover(string path)
    {
        long ticks = GetWriteTicks(path);
        lock (s_coverLock)
        {
            if (s_covers.TryGetValue(path, out var cached) && cached.Ticks == ticks && cached.Sprite != null && !cached.Sprite.WasCollected)
            {
                TouchLru(s_coverLru, path);
                return cached.Sprite;
            }
            // 캐시에 있던 스프라이트가 수집됐거나 파일이 수정됐으면 항목을 지운다.
            if (s_covers.Remove(path, out var stale))
            {
                s_coverLru.Remove(path);
                DestroySprite(stale.Sprite);
            }
            if (s_failedCovers.TryGetValue(path, out var failedTicks) && failedTicks == ticks)
                return null; // 수정되지 않은 실패 파일은 매 프레임 재시도하지 않는다
            s_failedCovers[path] = ticks;

            // 캐시 한도 초과 시 가장 오래된 텍스처/스프라이트 메모리 명시적 해제 (GPU VRAM 누수 차단)
            while (s_covers.Count >= MaxCoverCache && s_coverLru.First != null)
            {
                var oldest = s_coverLru.First.Value;
                s_coverLru.RemoveFirst();
                if (s_covers.Remove(oldest, out var oldEntry))
                    DestroySprite(oldEntry.Sprite);
            }
        }

        var bytes = File.ReadAllBytes(path);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        if (!LoadImage(tex, bytes))
        {
            Core.Log.Warning($"[CustomMedia] 이미지 로드 실패: {path}");
            UnityEngine.Object.Destroy(tex);
            return null;
        }
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        int cacheCount;
        lock (s_coverLock)
        {
            s_covers[path] = new CoverEntry { Ticks = ticks, Sprite = sprite };
            s_coverLru.AddLast(path);
            s_failedCovers.Remove(path);
            cacheCount = s_covers.Count;
        }
        Core.Log.Msg($"[CustomMedia] 커버 로드: {path} ({tex.width}x{tex.height}) [캐시 {cacheCount}/{MaxCoverCache}]");
        return sprite;
    }
}

[HarmonyPatch(typeof(CoverManager), nameof(CoverManager.GetCoverSprite))]
internal static class CoverManager_GetCoverSprite_Patch
{
    private const string Hook = "CoverManager.GetCoverSprite";

    private static void Postfix(string songId, ref Sprite __result)
    {
        // 선곡 화면에서 모든 곡의 배너마다 불린다 → 커스텀 ID가 아니면 바로 끝낸다
        if (HookGuard.IsDisabled(Hook) || !CustomSongs.IsCustomId(songId)) return;
        var song = CustomSongs.ById(songId);
        var path = song?.ThumbnailPath;
        if (path == null) return;
        try
        {
            var sprite = CustomMedia.GetCover(path);
            if (sprite != null) __result = sprite;
        }
        catch (Exception e)
        {
            if (!HookGuard.Fail(Hook, e)) Core.Log.Error($"[CustomMedia] 커버 처리 실패: {e}");
        }
    }
}

[HarmonyPatch(typeof(PreviewSoundController), nameof(PreviewSoundController.Play))]
internal static class PreviewSoundController_Play_Patch
{
    private const string Hook = "PreviewSoundController.Play";

    private static void Prefix(string songId)
    {
        CustomSongs.Current = null; // 선곡 화면으로 돌아왔으므로 플레이 중 곡 해제
        if (HookGuard.IsDisabled(Hook)) return;
        try
        {
            var song = CustomSongs.ById(songId);
            song?.RefreshMedia(); // 곡 폴더에 커버·음원을 추가했으면 다시 확인
            CustomMedia.PreviewSong = song;
        }
        catch (Exception e) { HookGuard.Fail(Hook, e); }
    }
}

/// <summary>
/// 미리듣기/인게임 BGM 교체. PreviewSoundController의 로더와 PatternPlayer.SetMainTrackToPlay는 실측상 패치가 발동하지 않고
/// SoundData 생성자는 IL2CPP 패치 백엔드 초기화에 실패해서, 모든 음원이 거치는 정적 SoundData.FromHEAudioClip에서 바꾼다.
/// 커스텀 곡 리소스 키는 AddressableKeyRedirect로 기반 곡 것을 로드하므로, 클립 이름에 기반 곡 ID가 들어 있으면
/// (미리듣기 "wits_biz", BGM "wits_biz_156bpm" 등) 해당 곡의 music.ogg 클립으로 바꾼다.
/// </summary>
[HarmonyPatch(typeof(SoundData), nameof(SoundData.FromHEAudioClip), typeof(HEAudioClip), typeof(bool), typeof(Il2CppSystem.Action))]
internal static class SoundData_FromHEAudioClip_Patch
{
    private const string Hook = "SoundData.FromHEAudioClip";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> s_seen = new();

    /// <summary>
    /// 효과음을 포함한 모든 음원 생성에서 불린다. 커스텀 곡이 선택되지 않았으면 클립을 건드리지 않고,
    /// 클립 이름 읽기 등 Unity/게임 멤버 접근은 TryReplace로 격리한다 (HookGuard 설명 참고).
    /// </summary>
    private static void Prefix(ref HEAudioClip audioClip, bool isLooping, ref Il2CppSystem.Action afterDispose)
    {
        var song = CustomSongs.Current?.Song ?? CustomMedia.PreviewSong;
        var baseId = CustomSongs.s_baseSongId;
        if (song == null || baseId == null || audioClip == null || HookGuard.IsDisabled(Hook)) return;
        var path = song.MusicPath;
        if (path == null) return;
        try { TryReplace(ref audioClip, ref afterDispose, baseId, path); }
        catch (Exception e)
        {
            if (!HookGuard.Fail(Hook, e)) Core.Log.Error($"[CustomMedia] 음원 교체 실패: {e}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("SoundData.FromHEAudioClip")]
    private static void TryReplace(ref HEAudioClip audioClip, ref Il2CppSystem.Action afterDispose, string baseId, string path)
    {
        var name = audioClip.name ?? "";
        if (s_seen.TryAdd(name, 0)) Core.Log.Msg($"[CustomMedia] 음원 로드 감지: '{name}'");
        if (!name.Contains(baseId, StringComparison.Ordinal)) return;
        var customClip = CustomMedia.GetClip(path);
        if (customClip == null) return;
        audioClip = customClip;
        ModHealth.Mark("Music");

        // afterDispose 실행 중 예외를 경고 로그로 격리하는 try-catch 래퍼 (GC 수명 대책은 아님)
        if (afterDispose != null) afterDispose = UpdateResilience.WrapAction(afterDispose);

        Core.Log.Msg($"[CustomMedia] 음원 '{name}' -> {path} ({(CustomSongs.Current != null ? "인게임" : "미리듣기")})");
    }
}

[HarmonyPatch(typeof(BGAPlayer), nameof(BGAPlayer.SetClip))]
internal static class BGAPlayer_SetClip_Patch
{
    private const string Hook = "BGAPlayer.SetClip";

    private static void Postfix(BGAPlayer __instance, bool isLooping)
    {
        var path = CustomSongs.Current?.Song.VideoPath;
        if (path == null || __instance == null || HookGuard.IsDisabled(Hook)) return;
        try { Apply(__instance, isLooping, path); }
        catch (Exception e)
        {
            if (!HookGuard.Fail(Hook, e)) Core.Log.Error($"[CustomMedia] BGA 처리 실패: {e}");
        }
    }

    // _videoPlayer 등 게임 필드 접근 격리 (일반 곡의 BGA 설정도 이 훅을 지나간다)
    [MethodImpl(MethodImplOptions.NoInlining), Guarded("BGAPlayer.SetClip")]
    private static void Apply(BGAPlayer bga, bool isLooping, string path)
    {
        var player = bga._videoPlayer;
        if (player == null || player.WasCollected) return;
        player.source = UnityEngine.Video.VideoSource.Url;
        CustomMedia.SetUrl(player, new Uri(path).AbsoluteUri);
        ModHealth.Mark("Video");
        player.isLooping = isLooping;
        player.Prepare();
        Core.Log.Msg($"[CustomMedia] BGA <- {path}");
    }
}
