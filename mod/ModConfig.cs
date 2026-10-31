using System.Globalization;
using MelonLoader.Utils;
using UnityEngine;

namespace PLATiNALAB.Mod;

/// <summary>
/// SaveCustomKey\config.txt — sxtg2(STARTRAIL 모드)와 같은 형식의 설정 파일.
/// 곡을 시작할 때(PlayDirector.Awake)마다 다시 읽으므로 게임을 재시작하지 않아도 다음 곡부터 반영된다.
/// 값: 1/0, true/false, 켜짐/꺼짐, on/off. # 또는 // 로 시작하는 줄은 주석.
/// </summary>
internal static class ModConfig
{
    public static bool AutoPlay;
    public static bool AllPerfect;

    public static bool EnableJudgmentBar = true;
    public static bool JudgmentBarVertical = true;
    public static int JudgmentBarShape;
    public static int JudgmentBarRangeShape = -1;
    public static string JudgmentBarSide = "Center";

    public static bool EnableKeyViewer = true;
    public static Color KeyViewerPressedColor = new(0.15f, 0.75f, 0.85f, 0.85f);
    public static Color KeyViewerNormalColor = new(0.08f, 0.08f, 0.08f, 0.65f);
    public static Color KeyViewerPitchPressedColor = new(1f, 0.25f, 0.5f, 0.9f);

    public static string FilePath => Path.Combine(MelonEnvironment.GameRootDirectory, "SaveCustomKey", "config.txt");

    private const string DefaultText = @"# PLATiNA LAB 모드 설정 파일 (SaveCustomKey/config.txt)
# 저장하면 다음 곡(곡을 시작할 때)부터 적용됩니다. 게임을 다시 켤 필요는 없습니다.
# 켜기/끄기 값: 1/0, true/false, 켜짐/꺼짐, on/off 모두 됩니다. # 또는 // 로 시작하는 줄은 주석입니다.

# ===== [플레이] =====

# 오토 플레이 (1 = 켜짐, 0 = 꺼짐)
# 게임 자체의 오토 플레이 모드(인게임 설정 Extra = AUTO PLAY)를 플레이하는 동안만 켭니다.
# 이 모드에서는 게임이 기록을 남기지 않습니다. 게임 설정에는 저장되지 않습니다.
AutoPlay=0

# 올 퍼펙트 판정 조작 - PERFECT HIGH (1 = 켜짐, 0 = 꺼짐)
# 서버 랭킹이 있는 게임이라 hwa 폴더 커스텀 곡에서만 동작합니다(커스텀 곡은 서버에 기록을 보내지 않음).
AllPerfect=0

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
# 형식: #RRGGBB, #RRGGBBAA, R,G,B,A, 색 이름 (시안, 마젠타, 노랑, 빨강, 파랑, 초록, 흰색, 검정, 주황, 보라, 분홍, 하늘, 민트)
KeyViewerPressedColor=#26BFD9D9

# 누르지 않았을 때 색
KeyViewerNormalColor=#141414A6

# 피치 키를 눌렀을 때 색
KeyViewerPitchPressedColor=#FF4081E6
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
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                Apply(line[..eq].Trim(), line[(eq + 1)..].Trim());
            }
            Core.Log.Msg($"[Config] {reason}: AutoPlay={AutoPlay} AllPerfect={AllPerfect} JudgmentBar={EnableJudgmentBar} KeyViewer={EnableKeyViewer}");
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[Config] config.txt 읽기 실패, 이전 값 유지: {e.Message}");
        }
    }

    private static void Apply(string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "autoplay": AutoPlay = Bool(value, AutoPlay); break;
            case "allperfect": AllPerfect = Bool(value, AllPerfect); break;
            case "enablejudgmentbar": EnableJudgmentBar = Bool(value, EnableJudgmentBar); break;
            case "judgmentbarvertical": JudgmentBarVertical = Bool(value, JudgmentBarVertical); break;
            case "judgmentbarshape": JudgmentBarShape = Int(value, JudgmentBarShape, 0, 2); break;
            case "judgmentbarrangeshape": JudgmentBarRangeShape = Int(value, JudgmentBarRangeShape, -1, 2); break;
            case "judgmentbarside": JudgmentBarSide = value; break;
            case "enablekeyviewer": EnableKeyViewer = Bool(value, EnableKeyViewer); break;
            case "keyviewerpressedcolor": KeyViewerPressedColor = ColorOf(value, KeyViewerPressedColor); break;
            case "keyviewernormalcolor": KeyViewerNormalColor = ColorOf(value, KeyViewerNormalColor); break;
            case "keyviewerpitchpressedcolor": case "keyviewergatepressedcolor":
                KeyViewerPitchPressedColor = ColorOf(value, KeyViewerPitchPressedColor); break;
        }
    }

    private static bool Bool(string v, bool fallback) => v.ToLowerInvariant() switch
    {
        "1" or "true" or "on" or "켜짐" or "켬" or "yes" => true,
        "0" or "false" or "off" or "꺼짐" or "끔" or "no" => false,
        _ => fallback,
    };

    private static int Int(string v, int fallback, int min, int max) =>
        int.TryParse(v, out var n) ? Math.Clamp(n, min, max) : fallback;

    private static Color ColorOf(string v, Color fallback)
    {
        v = v.Trim();
        switch (v)
        {
            case "시안": return new Color(0f, 1f, 1f);
            case "마젠타": return new Color(1f, 0f, 1f);
            case "노랑": return new Color(1f, 0.92f, 0.016f);
            case "빨강": return new Color(1f, 0f, 0f);
            case "파랑": return new Color(0f, 0f, 1f);
            case "초록": return new Color(0f, 1f, 0f);
            case "흰색": return new Color(1f, 1f, 1f);
            case "검정": return new Color(0f, 0f, 0f);
            case "주황": return new Color(1f, 0.5f, 0f);
            case "보라": return new Color(0.5f, 0f, 1f);
            case "분홍": return new Color(1f, 0.6f, 0.8f);
            case "하늘": return new Color(0.53f, 0.81f, 0.98f);
            case "민트": return new Color(0.6f, 1f, 0.8f);
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
            for (int i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return fallback;
            // 0~255로 적었으면 0~1로
            if (f.Take(parts.Length).Any(x => x > 1f)) for (int i = 0; i < parts.Length; i++) f[i] /= 255f;
            return new Color(f[0], f[1], f[2], f[3]);
        }
        return fallback;
    }
}
