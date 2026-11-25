using System.Globalization;
using MelonLoader.Utils;
using UnityEngine;

namespace PLATiNALAB.Mod;

/// <summary>
/// SaveCustomKey\config.txt — sxtg2(STARTRAIL 모드)와 같은 형식의 설정 파일.
/// 곡을 시작할 때(PlayDirector.Awake)마다 다시 읽으므로 게임을 재시작하지 않아도 다음 곡부터 반영된다.
/// 값: 1/0, true/false, 켜짐/꺼짐, on/off, 开/关. # 또는 // 로 시작하는 줄은 주석.
/// 다국어 지원: 한국어, 영어, 중국어(간체/번체).
/// </summary>
internal static class ModConfig
{
    public static bool AutoPlay;
    public static bool AllPerfect;
    public static bool DiagnosticLogs;

    /// <summary>완전 오프라인 독립 실행 모드 (기본 꺼짐). 시작 시 한 번만 읽는다 — <see cref="Offline"/> 참고.</summary>
    public static bool OfflineMode;
    public const string DefaultOfflineNickname = "PLAYER";
    public const string DefaultOfflineUserId = "76561197960265729";
    public static string OfflineNickname = DefaultOfflineNickname;
    public static string OfflineUserId = DefaultOfflineUserId;

    public static bool EnableJudgmentBar = true;
    public static bool JudgmentBarVertical = true;
    public static int JudgmentBarShape;
    public static int JudgmentBarRangeShape = -1;
    public static string JudgmentBarSide = "Center";

    /// <summary>판정바·키뷰어 크기 배율 (기본 1, 범위 0.5~4). 4K 등 고해상도에서 키울 때 쓴다.</summary>
    public static float OverlayScale = 1f;

    public static bool EnableKeyViewer = true;
    private static readonly Color DefaultPressedColor = new(0.15f, 0.75f, 0.85f, 0.85f);
    private static readonly Color DefaultNormalColor = new(0.08f, 0.08f, 0.08f, 0.65f);
    private static readonly Color DefaultPitchPressedColor = new(1f, 0.25f, 0.5f, 0.9f);
    public static Color KeyViewerPressedColor = DefaultPressedColor;
    public static Color KeyViewerNormalColor = DefaultNormalColor;
    public static Color KeyViewerPitchPressedColor = DefaultPitchPressedColor;

    public static string FilePath => Path.Combine(MelonEnvironment.GameRootDirectory, "SaveCustomKey", "config.txt");

    private static long s_lastWriteTicks = long.MinValue;

    private static readonly HashSet<string> s_warned = new(StringComparer.OrdinalIgnoreCase);

    private static void WarnOnce(string key, string msg)
    {
        if (s_warned.Add(key)) Core.Log.Warning($"[Config] {msg}");
    }

    private const string DefaultText = @"# PLATiNA LAB 모드 설정 파일 (SaveCustomKey/config.txt)
# 저장하면 다음 곡(곡을 시작할 때)부터 적용됩니다. 게임을 다시 켤 필요는 없습니다.
# 켜기/끄기 값: 1/0, true/false, 켜짐/꺼짐, on/off, 开/关 모두 됩니다. # 또는 // 로 시작하는 줄은 주석입니다.

# ===== [플레이] =====

# 오토 플레이 (1 = 켜짐, 0 = 꺼짐)
# 게임 자체의 오토 플레이 모드(인게임 설정 Extra = AUTO PLAY)를 플레이하는 동안만 켭니다.
# 이 모드에서는 게임이 기록을 남기지 않습니다. 게임 설정에는 저장되지 않습니다.
AutoPlay=0

# 올 퍼펙트 판정 조작 - PERFECT HIGH (1 = 켜짐, 0 = 꺼짐)
# 서버 랭킹이 있는 게임이라 hwa 폴더 커스텀 곡에서만 동작합니다(커스텀 곡은 서버에 기록을 보내지 않음).
AllPerfect=0

# ===== [화면] =====

# 판정바·키뷰어 크기 배율 (기본 1, 범위 0.5~4). 4K 등 고해상도에서 작아 보이면 2 정도로 올리세요.
OverlayScale=1

# ===== [판정바] =====

# 실시간 판정바 표시 (1 = 켜짐, 0 = 꺼짐)
EnableJudgmentBar=1

# 판정바 방향 (1 = 세로, 0 = 가로)
JudgmentBarVertical=1

# 판정바 모양 (0 = 사각 바, 1 = 알약 캡슐, 2 = 삼각/다이아몬드)
JudgmentBarShape=0

# 판정바 안쪽 범위 박스 모양 (-1 = 판정바 모양 따라감, 0 = 사각, 1 = 알약, 2 = 삼각)
JudgmentBarRangeShape=-1

# 판정바 위치 (Left = 화면 왼쪽, Right = 화면 오른쪽, Center = 기본 위치)
# 기본 위치는 세로 판정바는 왼쪽, 가로 판정바는 화면 정중앙입니다.
JudgmentBarSide=Center

# ===== [키뷰어] =====

# 실시간 키뷰어 표시 (1 = 켜짐, 0 = 꺼짐)
EnableKeyViewer=1

# 키를 눌렀을 때 색 (일반 노트 키)
# 형식: #RRGGBB, #RRGGBBAA, R,G,B,A, 색 이름 (시안, 마젠타, 노랑, 빨강, 파랑, 초록, 흰색, 검정, 주황, 보라, 분홍, 하늘, 민트 / 红, 蓝, 绿, 黄, 白, 黑, 橙, 紫, 粉 등)
KeyViewerPressedColor=#26BFD9D9

# 누르지 않았을 때 색
KeyViewerNormalColor=#141414A6

# 피치 키를 눌렀을 때 색
KeyViewerPitchPressedColor=#FF4081E6

# ===== [오프라인] =====

# 완전 오프라인 독립 실행 모드 (1 = 켜짐, 0 = 꺼짐). 게임을 시작할 때만 읽습니다 (바꾸면 게임 재시작).
# 켜면 스팀 클라이언트·로그인·게임 서버 없이 실행합니다. 모든 곡의 기록이 서버로 가지 않습니다.
# 공식 곡의 보유·해금 상태는 이 모드를 끈 채(0) 스팀으로 온라인 실행했을 때 기록된 값을 그대로 씁니다
# (UserData\PLATiNALAB\song_ownership.json). 기록이 없는 곡은 잠긴 상태로 보입니다.
OfflineMode=0

# 오프라인 모드에서 쓸 닉네임과 사용자 ID(스팀 ID 64비트 숫자)
OfflineNickname=PLAYER
OfflineUserId=76561197960265729

# ===== [디버그] =====

# 진단용 상세 로그 패치 활성화 (1 = 켜짐, 0 = 꺼짐)
DiagnosticLogs=0
";

    public static void Load(string reason)
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, DefaultText, new System.Text.UTF8Encoding(true));
                Core.Log.Msg($"[Config] 기본 설정 파일 생성: {path}");
            }
            long ticks = File.GetLastWriteTimeUtc(path).Ticks;
            if (ticks == s_lastWriteTicks) return;

            // 먼저 끝까지 읽고 나서 초기화한다. 편집기가 저장하는 중이라 읽기가 실패하면(공유 위반 등) 이전 값을 그대로 둔다.
            // (예전에는 초기화 뒤에 읽어서, 읽기 실패 시 로그는 "이전 값 유지"인데 실제로는 기본값 — 예: AutoPlay가 꺼짐 — 이 됐다)
            var lines = EncodingHelper.ReadAllLinesSafe(path);
            // 파일에서 지운 줄은 기본값으로 돌아가야 한다 (예전에는 이전에 읽은 값이 게임을 끌 때까지 남았다)
            ResetToDefaults();
            foreach (var raw in lines)
            {
                var line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                Apply(line[..eq].Trim(), line[(eq + 1)..].Trim());
            }
            s_lastWriteTicks = ticks;
            if (DiagnosticLogs && Core.Patcher != null) DiagnosticPatches.Apply(Core.Patcher);
            if (Offline.Initialized && OfflineMode != Offline.Enabled)
                WarnOnce("offline-restart", $"OfflineMode={(OfflineMode ? 1 : 0)}은(는) 게임을 다시 시작해야 적용됩니다 (지금은 {(Offline.Enabled ? 1 : 0)}).");
            Core.Log.Msg($"[Config] {reason}: AutoPlay={AutoPlay} AllPerfect={AllPerfect} JudgmentBar={EnableJudgmentBar} KeyViewer={EnableKeyViewer}");
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[Config] config.txt 읽기 실패, 이전 값 유지: {e.Message}");
        }
    }

    private static void ResetToDefaults()
    {
        AutoPlay = false;
        AllPerfect = false;
        DiagnosticLogs = false;
        OfflineMode = false;
        OfflineNickname = DefaultOfflineNickname;
        OfflineUserId = DefaultOfflineUserId;
        OverlayScale = 1f;
        EnableJudgmentBar = true;
        JudgmentBarVertical = true;
        JudgmentBarShape = 0;
        JudgmentBarRangeShape = -1;
        JudgmentBarSide = "Center";
        EnableKeyViewer = true;
        KeyViewerPressedColor = DefaultPressedColor;
        KeyViewerNormalColor = DefaultNormalColor;
        KeyViewerPitchPressedColor = DefaultPitchPressedColor;
    }

    private static void Apply(string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "autoplay": AutoPlay = Bool(key, value, AutoPlay); break;
            case "allperfect": AllPerfect = Bool(key, value, AllPerfect); break;
            case "diagnosticlogs": DiagnosticLogs = Bool(key, value, DiagnosticLogs); break;
            case "offlinemode": OfflineMode = Bool(key, value, OfflineMode); break;
            case "offlinenickname": OfflineNickname = Text(key, value, DefaultOfflineNickname, 32); break;
            case "offlineuserid":
                if (ulong.TryParse(value, out _)) OfflineUserId = value;
                else WarnOnce($"val:{key}={value}", $"'{key}'의 값 '{value}'은(는) 숫자(스팀 ID 64비트)여야 합니다. 기본값을 사용합니다.");
                break;
            case "overlayscale":OverlayScale = Float(key, value, OverlayScale, 0.5f, 4f); break;
            case "enablejudgmentbar": EnableJudgmentBar = Bool(key, value, EnableJudgmentBar); break;
            case "judgmentbarvertical": JudgmentBarVertical = Bool(key, value, JudgmentBarVertical); break;
            case "judgmentbarshape": JudgmentBarShape = Int(key, value, JudgmentBarShape, 0, 2); break;
            case "judgmentbarrangeshape": JudgmentBarRangeShape = Int(key, value, JudgmentBarRangeShape, -1, 2); break;
            case "judgmentbarside": JudgmentBarSide = Side(key, value, JudgmentBarSide); break;
            case "enablekeyviewer": EnableKeyViewer = Bool(key, value, EnableKeyViewer); break;
            case "keyviewerpressedcolor": KeyViewerPressedColor = ColorOf(key, value, KeyViewerPressedColor); break;
            case "keyviewernormalcolor": KeyViewerNormalColor = ColorOf(key, value, KeyViewerNormalColor); break;
            case "keyviewerpitchpressedcolor": case "keyviewergatepressedcolor":
                KeyViewerPitchPressedColor = ColorOf(key, value, KeyViewerPitchPressedColor); break;
            default:
                WarnOnce($"key:{key}", $"알 수 없는 설정 키 '{key}' (무시됨)");
                break;
        }
    }

    private static string Side(string key, string v, string fallback)
    {
        if (string.Equals(v, "Left", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "Right", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "Center", StringComparison.OrdinalIgnoreCase))
            return v;
        WarnOnce($"val:{key}={v}", $"'{key}'의 값 '{v}'이(가) 잘못되었습니다 (Left/Right/Center 중 하나). 기본값 '{fallback}'을 사용합니다.");
        return fallback;
    }

    private static string Text(string key, string v, string fallback, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(v)) return fallback;
        if (v.Length > maxLength)
        {
            WarnOnce($"range:{key}", $"'{key}'의 값이 {maxLength}자를 넘어 앞 {maxLength}자만 사용합니다.");
            return v[..maxLength];
        }
        return v;
    }

    private static bool Bool(string key, string v, bool fallback)
    {
        switch (v.ToLowerInvariant())
        {
            case "1" or "true" or "on" or "켜짐" or "켬" or "yes" or "开" or "开启" or "是" or "对": return true;
            case "0" or "false" or "off" or "꺼짐" or "끔" or "no" or "关" or "关闭" or "否" or "错": return false;
            default:
                WarnOnce($"val:{key}={v}", $"'{key}'의 값 '{v}'을(를) 켜기/끄기로 해석할 수 없어 기본값({fallback})을 사용합니다.");
                return fallback;
        }
    }

    private static int Int(string key, string v, int fallback, int min, int max)
    {
        if (int.TryParse(v, out var n))
        {
            if (n < min || n > max)
                WarnOnce($"range:{key}={v}", $"'{key}'의 값 {n}이(가) 범위({min}~{max})를 벗어나 {Math.Clamp(n, min, max)}(으)로 조정합니다.");
            return Math.Clamp(n, min, max);
        }
        WarnOnce($"val:{key}={v}", $"'{key}'의 값 '{v}'이(가) 정수가 아니어서 기본값({fallback})을 사용합니다.");
        return fallback;
    }

    private static float Float(string key, string v, float fallback, float min, float max)
    {
        if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && !float.IsNaN(n) && !float.IsInfinity(n))
        {
            if (n < min || n > max)
                WarnOnce($"range:{key}={v}", $"'{key}'의 값 {n}이(가) 범위({min}~{max})를 벗어나 {Math.Clamp(n, min, max)}(으)로 조정합니다.");
            return Math.Clamp(n, min, max);
        }
        WarnOnce($"val:{key}={v}", $"'{key}'의 값 '{v}'을(를) 숫자로 읽을 수 없어 기본값({fallback})을 사용합니다.");
        return fallback;
    }

    private static Color ColorOf(string key, string v, Color fallback)
    {
        v = v.Trim();
        switch (v.ToLowerInvariant())
        {
            case "시안" or "cyan" or "青" or "青色": return new Color(0f, 1f, 1f);
            case "마젠타" or "magenta" or "品红" or "洋红": return new Color(1f, 0f, 1f);
            case "노랑" or "yellow" or "黄" or "黄色": return new Color(1f, 0.92f, 0.016f);
            case "빨강" or "red" or "红" or "红色": return new Color(1f, 0f, 0f);
            case "파랑" or "blue" or "蓝" or "蓝色": return new Color(0f, 0f, 1f);
            case "초록" or "green" or "绿" or "绿色": return new Color(0f, 1f, 0f);
            case "흰색" or "white" or "白" or "白色": return new Color(1f, 1f, 1f);
            case "검정" or "black" or "黑" or "黑色": return new Color(0f, 0f, 0f);
            case "주황" or "orange" or "橙" or "橙色": return new Color(1f, 0.5f, 0f);
            case "보라" or "purple" or "紫" or "紫色": return new Color(0.5f, 0f, 1f);
            case "분홍" or "pink" or "粉" or "粉色": return new Color(1f, 0.6f, 0.8f);
            case "하늘" or "sky" or "skyblue" or "天蓝" or "天蓝色": return new Color(0.53f, 0.81f, 0.98f);
            case "민트" or "mint" or "薄荷" or "薄荷绿": return new Color(0.6f, 1f, 0.8f);
        }
        if (v.StartsWith("#") && (v.Length == 7 || v.Length == 9) &&
            uint.TryParse(v[1..], NumberStyles.HexNumber, null, out var hex))
        {
            if (v.Length == 7) hex = (hex << 8) | 0xFF;
            return new Color(((hex >> 24) & 0xFF) / 255f, ((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);
        }
        var parts = v.Split(',');
        if (parts.Length is 3 or 4)
        {
            var f = new float[4] { 0, 0, 0, 1 };
            bool ok = true;
            for (int i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) { ok = false; break; }
            if (ok)
            {
                // 0~255로 적었으면 0~1로
                bool scale255 = false;
                for (int i = 0; i < parts.Length; i++)
                    if (f[i] > 1f) { scale255 = true; break; }
                if (scale255) for (int i = 0; i < parts.Length; i++) f[i] /= 255f;
                return new Color(f[0], f[1], f[2], f[3]);
            }
        }
        WarnOnce($"val:{key}={v}", $"'{key}'의 색상 값 '{v}'을(를) 해석할 수 없어 기본값을 사용합니다.");
        return fallback;
    }
}
