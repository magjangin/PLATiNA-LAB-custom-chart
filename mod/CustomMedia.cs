using HarmonyLib;
using Il2CppHighendGames.HEAudio;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPlatinaLab.Main.Catalog;
using Il2CppPlatinaLab.Main.InGame;
using Il2CppPlatinaLab.Main.Select.Preview.Controller;
using Il2CppPlatinaLab.Main.UI.InGame;
using Il2CppCysharp.Threading.Tasks;
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
    private static readonly Dictionary<string, Sprite> s_covers = new();
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
    /// 인터롭의 ImageConversion.LoadImage 오버로드는 전부 Il2CppSystem.ReadOnlySpan 경로로 가서
    /// MissingMethodException(ReadOnlySpan.GetPinnableReference)이 난다(실측). 래퍼 본문을 그대로 재현해 icall을 직접 부른다.
    /// </summary>
    private static unsafe bool LoadImage(Texture2D tex, byte[] bytes)
    {
        var texPtr = UnityEngine.Object.MarshalledUnityObject.MarshalNotNull(tex);
        fixed (byte* begin = bytes)
        {
            var span = new UnityEngine.Bindings.ManagedSpanWrapper(begin, bytes.Length);
            return ImageConversion.LoadImage_Injected(texPtr, ref span, false);
        }
    }

    /// <summary>VideoPlayer.url 세터도 Il2CppSystem.ReadOnlySpan 경로라 깨진다(실측) → icall 직접 호출.</summary>
    public static unsafe void SetUrl(UnityEngine.Video.VideoPlayer player, string url)
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

    public static Sprite GetCover(string path)
    {
        if (s_covers.TryGetValue(path, out var cached) && cached != null && !cached.WasCollected) return cached;
        if (!s_failedCovers.Add(path)) return null; // 실패한 파일은 매 프레임 재시도하지 않는다

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        if (!LoadImage(tex, File.ReadAllBytes(path)))
        {
            Core.Log.Warning($"[CustomMedia] 이미지 로드 실패: {path}");
            return null;
        }
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        s_covers[path] = sprite;
        s_failedCovers.Remove(path);
        Core.Log.Msg($"[CustomMedia] 커버 로드: {path} ({tex.width}x{tex.height})");
        return sprite;
    }
}

[HarmonyPatch(typeof(CoverManager), nameof(CoverManager.GetCoverSprite))]
internal static class CoverManager_GetCoverSprite_Patch
{
    private static void Postfix(string songId, ref Sprite __result)
    {
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
// SoundData 생성자는 Il2CppInterop이 네이티브 패치에 실패한다(로그: "Failed to init IL2CPP patch backend for SoundData::.ctor").
// 그래서 생성자를 부르는 정적 팩토리 FromHEAudioClip(HEAudioClip, bool, Action)에 건다 (비동기 로더도 이것을 거침).
[HarmonyPatch(typeof(SoundData), nameof(SoundData.FromHEAudioClip), typeof(HEAudioClip), typeof(bool), typeof(Il2CppSystem.Action))]
internal static class SoundData_FromHEAudioClip_Patch
{
    private static readonly HashSet<string> s_seen = new();

    private static void Prefix(ref HEAudioClip audioClip)
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
            var player = __instance._videoPlayer;
            if (player == null) return;
            player.source = UnityEngine.Video.VideoSource.Url;
            CustomMedia.SetUrl(player, new Uri(path).AbsoluteUri);
            player.isLooping = isLooping;
            player.Prepare();
            Core.Log.Msg($"[CustomMedia] BGA <- {path}");
        }
        catch (Exception e) { Core.Log.Error($"[CustomMedia] BGA 처리 실패: {e}"); }
    }
}
