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
/// - music.ogg  → 미리듣기(PreviewSoundController) + 인게임 BGM(PatternPlayer.SetMainTrackToPlay)
/// - video.mp4  → BGAPlayer.SetClip 직후 VideoPlayer를 URL 모드로 전환
/// 게임 음원(.fmod.ogg)은 HEAudioClip(ScriptableObject)에 ogg/wav 바이트를 담은 것이고
/// new SoundData(clip, isLooping)으로 재생 객체를 만든다(HighendGames.HEAudio 인터롭 실측).
/// </summary>
internal static class CustomMedia
{
    private static readonly Dictionary<string, HEAudioClip> s_clips = new();
    private const int MaxCoverCache = 24;
    private static readonly Dictionary<string, Sprite> s_covers = new();
    private static readonly LinkedList<string> s_coverLru = new();
    private static readonly HashSet<string> s_failedCovers = new();

    public static HEAudioClip GetClip(string path)
    {
        if (!s_clips.TryGetValue(path, out var clip) || clip == null || clip.WasCollected)
        {
            var bytes = File.ReadAllBytes(path);
            clip = ScriptableObject.CreateInstance(Il2CppType.Of<HEAudioClip>()).Cast<HEAudioClip>();
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            clip._bytes = new Il2CppStructArray<byte>(bytes);
            // RIFF 헤더면 wav, 아니면 ogg
            clip._audioFormat = bytes.Length > 4 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
                ? AudioFormat.Wav : AudioFormat.Ogg;
            s_clips[path] = clip;
        }
        return clip;
    }

    /// <summary>
    /// 이미지 로드 이중 래퍼 (Double-Wrapper):
    /// 1차: Unity 공식 ImageConversion.LoadImage 시도 (MelonLoader/Unity 인터롭 정상화 시 동작)
    /// 2차: MissingMethodException(ReadOnlySpan 버그) 시 네이티브 icall(LoadImage_Injected) 직접 호출
    /// </summary>
    private static unsafe bool LoadImage(Texture2D tex, byte[] bytes)
    {
        if (tex == null || tex.WasCollected || bytes == null || bytes.Length == 0) return false;

        // 1차: 표준 공식 API 시도
        try
        {
            if (ImageConversion.LoadImage(tex, bytes)) return true;
        }
        catch (MissingMethodException) { /* ReadOnlySpan 이슈 발생 시 2차 폴백 진행 */ }
        catch (Exception e)
        {
            Core.Log.Warning($"[CustomMedia] 표준 LoadImage 예외 발생, icall 폴백 시도: {e.Message}");
        }

        // 2차: 네이티브 icall 직접 호출
        try
        {
            var texPtr = UnityEngine.Object.MarshalledUnityObject.MarshalNotNull(tex);
            fixed (byte* begin = bytes)
            {
                var span = new UnityEngine.Bindings.ManagedSpanWrapper(begin, bytes.Length);
                return ImageConversion.LoadImage_Injected(texPtr, ref span, false);
            }
        }
        catch (Exception e)
        {
            Core.Log.Error($"[CustomMedia] LoadImage icall 폴백 실패: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// VideoPlayer.url 설정 이중 래퍼:
    /// 1차: 표준 player.url 세터 시도
    /// 2차: ReadOnlySpan 버그로 실패 시 set_url_Injected icall 직접 호출
    /// </summary>
    public static unsafe void SetUrl(UnityEngine.Video.VideoPlayer player, string url)
    {
        if (player == null || player.WasCollected || string.IsNullOrEmpty(url)) return;

        // 1차: 표준 프로퍼티 시도
        try
        {
            player.url = url;
            return;
        }
        catch (MissingMethodException) { /* ReadOnlySpan 이슈 시 2차 폴백 진행 */ }
        catch (Exception e)
        {
            Core.Log.Warning($"[CustomMedia] 표준 VideoPlayer.url 예외 발생, icall 폴백 시도: {e.Message}");
        }

        // 2차: 네이티브 icall 직접 호출
        try
        {
            var self = UnityEngine.Object.MarshalledUnityObject.MarshalNotNull(player);
            fixed (char* begin = url)
            {
                var span = new UnityEngine.Bindings.ManagedSpanWrapper(begin, url.Length);
                UnityEngine.Video.VideoPlayer.set_url_Injected(self, ref span);
            }
        }
        catch (Exception e)
        {
            Core.Log.Error($"[CustomMedia] VideoPlayer.url icall 폴백 실패: {e.Message}");
        }
    }

    /// <summary>미리듣기 중인 커스텀 곡 (PreviewSoundController.Play에서 설정).</summary>
    public static CustomSong PreviewSong;

    public static Sprite GetCover(string path)
    {
        if (s_covers.TryGetValue(path, out var cached) && cached != null && !cached.WasCollected)
        {
            // LRU 갱신: 최근 사용된 항목을 뒤로 이동
            s_coverLru.Remove(path);
            s_coverLru.AddLast(path);
            return cached;
        }
        if (!s_failedCovers.Add(path)) return null; // 실패한 파일은 매 프레임 재시도하지 않는다

        // 캐시 한도 초과 시 가장 오래된 텍스처/스프라이트 메모리 명시적 해제 (GPU VRAM 누수 차단)
        while (s_covers.Count >= MaxCoverCache && s_coverLru.First != null)
        {
            var oldest = s_coverLru.First.Value;
            s_coverLru.RemoveFirst();
            if (s_covers.Remove(oldest, out var oldSprite) && oldSprite != null && !oldSprite.WasCollected)
            {
                try
                {
                    if (oldSprite.texture != null && !oldSprite.texture.WasCollected)
                        UnityEngine.Object.Destroy(oldSprite.texture);
                    UnityEngine.Object.Destroy(oldSprite);
                }
                catch { }
            }
        }

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        if (!LoadImage(tex, File.ReadAllBytes(path)))
        {
            Core.Log.Warning($"[CustomMedia] 이미지 로드 실패: {path}");
            UnityEngine.Object.Destroy(tex);
            return null;
        }
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        s_covers[path] = sprite;
        s_coverLru.AddLast(path);
        s_failedCovers.Remove(path);
        Core.Log.Msg($"[CustomMedia] 커버 로드: {path} ({tex.width}x{tex.height}) [캐시 {s_covers.Count}/{MaxCoverCache}]");
        return sprite;
    }
}

[HarmonyPatch(typeof(CoverManager), nameof(CoverManager.GetCoverSprite))]
internal static class CoverManager_GetCoverSprite_Patch
{
    private static void Postfix(string songId, ref Sprite __result)
    {
        if (string.IsNullOrEmpty(songId)) return;
        var song = CustomSongs.ById(songId);
        var path = song?.ThumbnailPath;
        if (path == null) return;
        try
        {
            var sprite = CustomMedia.GetCover(path);
            if (sprite != null) __result = sprite;
        }
        catch (Exception e) { Core.Log.Error($"[CustomMedia] 커버 처리 실패: {e}"); }
    }
}

[HarmonyPatch(typeof(PreviewSoundController), nameof(PreviewSoundController.Play))]
internal static class PreviewSoundController_Play_Patch
{
    private static void Prefix(string songId)
    {
        CustomSongs.Current = null; // 선곡 화면으로 돌아왔으므로 플레이 중 곡 해제
        CustomMedia.PreviewSong = CustomSongs.ById(songId);
    }
}

/// <summary>
/// 미리듣기/인게임 BGM 교체. PreviewSoundController의 로더와 PatternPlayer.SetMainTrackToPlay는 실측상 패치가 발동하지 않아
/// (인라인/다른 경로) 모든 음원이 거치는 SoundData 생성자에서 바꾼다.
/// 커스텀 곡 리소스 키는 AddressableKeyRedirect로 기반 곡 것을 로드하므로, 클립 이름에 기반 곡 ID가 들어 있으면
/// (미리듣기 "wits_biz", BGM "wits_biz_156bpm" 등) 해당 곡의 music.ogg 클립으로 바꾼다.
/// </summary>
[HarmonyPatch(typeof(SoundData), nameof(SoundData.FromHEAudioClip), typeof(HEAudioClip), typeof(bool), typeof(Il2CppSystem.Action))]
internal static class SoundData_FromHEAudioClip_Patch
{
    private static readonly HashSet<string> s_seen = new();

    private static void Prefix(ref HEAudioClip audioClip, bool isLooping, ref Il2CppSystem.Action afterDispose)
    {
        var baseId = CustomSongs.s_baseSong?.Id;
        if (baseId == null || audioClip == null) return;
        var song = CustomSongs.Current?.Song ?? CustomMedia.PreviewSong;
        var path = song?.MusicPath;
        if (path == null) return;
        try
        {
            var name = audioClip.name ?? "";
            if (s_seen.Add(name)) Core.Log.Msg($"[CustomMedia] 음원 로드 감지: '{name}'");
            if (!name.Contains(baseId)) return;
            audioClip = CustomMedia.GetClip(path);

            // afterDispose 실행 중 예외를 경고 로그로 격리하는 try-catch 래퍼 (GC 수명 대책은 아님)
            if (afterDispose != null)
            {
                afterDispose = UpdateResilience.WrapAction(afterDispose);
            }

            Core.Log.Msg($"[CustomMedia] 음원 '{name}' -> {path} ({(CustomSongs.Current != null ? "인게임" : "미리듣기")})");
        }
        catch (Exception e) { Core.Log.Error($"[CustomMedia] 음원 교체 실패: {e}"); }
    }
}

[HarmonyPatch(typeof(BGAPlayer), nameof(BGAPlayer.SetClip))]
internal static class BGAPlayer_SetClip_Patch
{
    private static void Postfix(BGAPlayer __instance, bool isLooping)
    {
        var path = CustomSongs.Current?.Song.VideoPath;
        if (path == null) return;
        try
        {
            var player = __instance?._videoPlayer;
            if (player == null || player.WasCollected) return;
            player.source = UnityEngine.Video.VideoSource.Url;
            CustomMedia.SetUrl(player, new Uri(path).AbsoluteUri);
            player.isLooping = isLooping;
            player.Prepare();
            Core.Log.Msg($"[CustomMedia] BGA <- {path}");
        }
        catch (Exception e) { Core.Log.Error($"[CustomMedia] BGA 처리 실패: {e}"); }
    }
}
