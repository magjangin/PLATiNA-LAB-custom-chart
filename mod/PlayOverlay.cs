using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppPlatinaLab.Main;
using Il2CppPlatinaLab.Main.InGame;
using Il2CppPlatinaLab.Main.InGame.Pattern.Note;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PLATiNALAB.Mod;

/// <summary>
/// 판정바 + 키뷰어 (sxtg2 JudgmentBarFeature / KeyViewerFeature 포팅).
/// sxtg2는 IMGUI(OnGUI)로 그렸지만 이 게임 빌드는 IMGUI가 스트리핑돼 있어(GUI.* 호출마다 "Method unstripping failed")
/// 화면 오버레이 Canvas + Image + TextMeshProUGUI로 그린다. 좌표는 픽셀(CanvasScaler 없음, 왼쪽 아래 기준).
///
/// 판정바: PlayAssist가 ProcessJudgeResult에서 JudgeResult.Error를 넘긴다. 게임은 error &lt; 0이면 FAST
///   (UpdateFastSlow → ScoreRecorderUtility.AddRecord(isFast: 0 &gt; error), 디스어셈블 확인) → 화면에는 +가 FAST가 되게 부호를 뒤집는다.
///   범위 박스는 Constants.JudgmentTimingMap(판정 → (앞, 뒤)). ValueTuple의 Item1/Item2 인터롭 게터는 쓰레기값을
///   돌려줘서(1E-311 등) 박스를 직접 언박싱해 double 두 개를 읽는다.
/// 키뷰어: PatternPlayer.ProcessInputEvent(InputEvent{KeyCode, IsPressed})에서 원시 키코드를 받고,
///   InputStateMachine.GetKeyBindingArray(LineMode)/GetPitchKey로 레인에 대응시킨다
///   (InputStateMachine.Interpret는 인라인돼서 후킹 불가). 키코드는 RawKeycode(상위 바이트 0xE0 = 확장키, 하위 = 스캔코드).
/// 게임 루프는 별도 스레드(HFTask)라 공유 상태는 lock으로 넘기고, UI는 메인 스레드(Core.OnUpdate)에서만 만진다.
/// </summary>
internal static class PlayOverlay
{
    public static volatile bool InPlay;

    // ---------------- 판정바 상태 ----------------
    private struct HitTick { public float OffsetMs; public float Time; public JudgmentType Judgment; }
    private static readonly object s_lock = new();
    private static readonly List<HitTick> s_pending = new();
    private static readonly List<HitTick> s_history = new();
    private static HitTick? s_last;
    private const float Duration = 1.5f;

    private static readonly JudgmentType[] Judgments = { JudgmentType.PerfectHigh, JudgmentType.Perfect, JudgmentType.Great, JudgmentType.Good };
    // 범위 (ms, 앞/뒤 중 큰 값). JudgmentTimingMap을 못 읽었을 때의 폴백 (실측 PerfectHigh ±25 / Perfect ±50 / Great ±80 / Good ±120)
    private static readonly float[] RangeMs = { 25f, 50f, 80f, 120f };
    private static bool s_rangeLoaded;

    // ---------------- 키뷰어 상태 ----------------
    private static readonly object s_keyLock = new();
    private static Dictionary<ushort, int> s_laneOfKey;  // 키코드 → 표시 칸 인덱스
    private static string[] s_labels = Array.Empty<string>();
    private static bool[] s_pressed = Array.Empty<bool>();
    private static bool[] s_pressedBuffer = Array.Empty<bool>(); // GC allocation 방지용 재사용 버퍼
    private static int s_pitchIndex = -1;
    private static LineModeType s_mode = LineModeType.None;

    // ---------------- UI ----------------
    private static GameObject s_root;
    private static Transform s_barGroup, s_keyGroup;
    private static bool s_rootActive, s_barActive, s_keyActive;
    private static Image s_track, s_center;
    private static readonly Image[] s_ranges = new Image[3];
    private static readonly List<Image> s_ticks = new();
    private static TextMeshProUGUI s_label, s_labelShadow;
    private static readonly List<(Image bg, Outline border, TextMeshProUGUI text)> s_keys = new();
    private static TMP_FontAsset s_font;
    private static float s_nextFontSearchTime;
    private static readonly Dictionary<(int, int, int), Sprite> s_shapeSprites = new();
    private static readonly HashSet<string> s_warned = new();

    // ---------------- 그리기 캐시 ----------------
    // 매 프레임 바뀌지 않는 부분(판정바 틀·범위·중앙선, 키 칸 위치·글자)은 화면 크기나 설정이 바뀔 때만 다시 설정한다.
    // UI 속성 설정은 하나하나가 IL2CPP 호출이고 문자열 설정은 IL2CPP 문자열 할당까지 일으킨다.
    // (record struct는 Nullable 특성을 요구해 CS0656 → 값 튜플 + 유효 플래그)
    private static (float screenW, float screenH, bool vertical, int trackShape, int rangeShape, string side, float r0, float r1, float r2, float r3) s_barLayout;
    private static bool s_barLayoutValid;
    private static int s_hitVersion, s_labelVersion = -1;
    private static int s_historyVersion, s_ticksPlacedVersion = -1; // 기록이 늘거나 만료될 때만 틱 위치를 다시 잡는다
    private static float s_screenW, s_screenH; // 프레임마다 한 번만 읽는다 (Screen.width/height는 IL2CPP 호출)
    private static (float screenW, float screenH, string[] labels, int pitchIndex) s_keyLayout; // labels는 참조 비교 (BindKeys마다 새 배열)
    private static bool s_keyLayoutValid;
    private static int[] s_keyDrawn = Array.Empty<int>(); // 칸별로 마지막에 그린 상태 (-1 = 미정, 0 = 뗌, 1 = 누름)
    private const int MaxPending = 256;

    public static void Reset()
    {
        lock (s_lock) { s_pending.Clear(); s_history.Clear(); s_last = null; }
        lock (s_keyLock) { s_laneOfKey = null; s_mode = LineModeType.None; s_pressed = Array.Empty<bool>(); s_labels = Array.Empty<string>(); }
        // 곡 시작마다 설정 파일을 다시 읽으므로(색·모양·위치) 그리기 캐시도 비운다
        s_barLayoutValid = false;
        s_keyLayoutValid = false;
        s_labelVersion = -1;
        s_ticksPlacedVersion = -1;
    }

    /// <summary>게임 루프 스레드에서 호출. error = 게임 단위(ms), 음수 = FAST.</summary>
    public static void RegisterHit(double error, JudgmentType judgment)
    {
        lock (s_lock)
        {
            // 메인 스레드가 비우지 못하는 상황(창 비활성 등)에서도 무한히 쌓이지 않게 오래된 것부터 버린다
            if (s_pending.Count >= MaxPending) s_pending.RemoveAt(0);
            s_pending.Add(new HitTick { OffsetMs = (float)-error, Judgment = judgment, Time = -1f });
        }
    }

    /// <summary>같은 경고는 한 번만 남긴다 (매 프레임 도는 코드라 로그 스팸 방지).</summary>
    private static void WarnOnce(string message)
    {
        if (s_warned.Add(message)) Core.Log.Warning(message);
    }

    /// <summary>Core.OnUpdate (메인 스레드).</summary>
    public static void Update()
    {
        try
        {
            bool showBar = InPlay && ModConfig.EnableJudgmentBar;
            bool showKeys = InPlay && ModConfig.EnableKeyViewer;
            bool showAny = showBar || showKeys;
            // 비플레이 화면에서는 Unity Object == null 판정(IL2CPP 호출)조차 생략한다
            if (!showAny && !s_rootActive) return;
            s_screenW = Screen.width;
            s_screenH = Screen.height;
            if (s_root == null)
            {
                if (!showAny) { s_rootActive = s_barActive = s_keyActive = false; return; }
                Build();
            }
            if (s_rootActive != showAny) { s_root.SetActive(showAny); s_rootActive = showAny; }
            if (s_barActive != showBar) { s_barGroup.gameObject.SetActive(showBar); s_barActive = showBar; }
            if (s_keyActive != showKeys) { s_keyGroup.gameObject.SetActive(showKeys); s_keyActive = showKeys; }
            if (showBar) UpdateJudgmentBar();
            if (showKeys) UpdateKeyViewer();
        }
        catch (Exception e) { WarnOnce($"[PlayOverlay] 갱신 오류: {e.Message}"); }
    }

    // ================= UI 생성 =================

    private static void Build()
    {
        s_root = new GameObject("PLATiNALAB.Overlay");
        UnityEngine.Object.DontDestroyOnLoad(s_root);
        s_root.hideFlags = HideFlags.HideAndDontSave;
        var canvas = s_root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        s_barGroup = Group("JudgmentBar");
        s_keyGroup = Group("KeyViewer");
        s_rootActive = true;
        s_barActive = true;
        s_keyActive = true;

        s_track = NewImage(s_barGroup, "Track");
        for (int i = 0; i < s_ranges.Length; i++) s_ranges[i] = NewImage(s_barGroup, $"Range{i}");
        s_center = NewImage(s_barGroup, "Center");
        s_labelShadow = NewText(s_barGroup, "LabelShadow", 16);
        s_label = NewText(s_barGroup, "Label", 16);
        Core.Log.Msg("[PlayOverlay] 오버레이 생성 (uGUI)");
    }

    private static Transform Group(string name)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>();
        go.transform.SetParent(s_root.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return go.transform;
    }

    private static Image NewImage(Transform parent, string name)
    {
        var go = new GameObject(name);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
        return img;
    }

    private static TextMeshProUGUI NewText(Transform parent, string name, float size)
    {
        var go = new GameObject(name);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        var font = FindFont();
        if (font != null) t.font = font;
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
        return t;
    }

    /// <summary>TMP 기본 폰트가 없으면 씬에 있는 게임 텍스트의 폰트를 빌린다 (3초 쿨다운으로 프레임 드랍 방지).</summary>
    private static TMP_FontAsset FindFont()
    {
        if (s_font != null && !s_font.WasCollected) return s_font;
        s_font = TryGetDefaultFont();
        if ((s_font == null || s_font.WasCollected) && Time.time >= s_nextFontSearchTime)
        {
            s_nextFontSearchTime = Time.time + 3.0f;
            // 1. 언어 설정(LocalizeTMPFont) 컴포넌트의 폰트 우선 탐색 (CJK/중국어 글리프 보장)
            if (!HookGuard.IsDisabled("PlayOverlay.LocalizeTMPFont"))
            {
                try { s_font = TryFindLocalizedFont(); }
                catch (Exception e) { HookGuard.Fail("PlayOverlay.LocalizeTMPFont", e); }
            }

            // 2. 일반 텍스트 폰트 폴백 (LocalizeTMPFont가 바뀌어 JIT 실패해도 이 폴백에 도달한다)
            if ((s_font == null || s_font.WasCollected) && !HookGuard.IsDisabled("PlayOverlay.AnyTMPFont"))
            {
                try { s_font = TryFindAnyTmpFont(); }
                catch (Exception e) { HookGuard.Fail("PlayOverlay.AnyTMPFont", e); }
            }
            if (s_font == null) WarnOnce("[PlayOverlay] TMP 폰트를 찾지 못함 — 글자가 안 보일 수 있음");
        }
        return s_font;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TMP_FontAsset TryGetDefaultFont()
    {
        try { return TMP_Settings.defaultFontAsset; }
        catch { return null; }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("PlayOverlay.LocalizeTMPFont")]
    private static TMP_FontAsset TryFindLocalizedFont()
    {
        var loc = UnityEngine.Object.FindObjectOfType<Il2CppPlatinaLab.Main.Common.Font.LocalizeTMPFont>();
        return loc != null && loc._text != null ? loc._text.font : null;
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("PlayOverlay.AnyTMPFont")]
    private static TMP_FontAsset TryFindAnyTmpFont()
    {
        var any = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
        return any != null ? any.font : null;
    }

    /// <summary>왼쪽 위 기준 픽셀 사각형(sxtg2 좌표계)을 캔버스(왼쪽 아래 기준)에 놓는다.</summary>
    private static void Place(Graphic g, Rect r)
    {
        var rt = g.rectTransform;
        rt.anchoredPosition = new Vector2(r.x, s_screenH - r.y - r.height);
        rt.sizeDelta = new Vector2(r.width, r.height);
    }

    private static void SetRect(Image img, Rect r, Color c, int shape = 0)
    {
        Place(img, r);
        img.color = c;
        img.sprite = shape == 0 ? null : ShapeSprite(shape, Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height));
        img.enabled = true;
    }

    // ================= 판정바 =================

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("PlayOverlay.LoadRanges")]
    private static unsafe void LoadRanges()
    {
        try
        {
            var map = Constants.JudgmentTimingMap;
            var text = new StringBuilder();
            var loaded = new float[RangeMs.Length];
            if (map == null) return;
            for (int i = 0; i < Judgments.Length; i++)
            {
                if (!map.ContainsKey(Judgments[i])) continue;
                var tuple = map[Judgments[i]];
                if (tuple == null || tuple.Pointer == IntPtr.Zero) continue;
                var data = (double*)IL2CPP.il2cpp_object_unbox(tuple.Pointer);
                if (data == null) continue;
                double early = data[0], late = data[1];
                text.Append($"{Judgments[i]}=({early},{late}) ");
                loaded[i] = (float)Math.Max(Math.Abs(early), Math.Abs(late));
            }
            // 값이 정상적이면 덮어쓰기
            bool sane = loaded.All(v => v > 1f && v < 1000f) && loaded.Zip(loaded.Skip(1), (a, b) => a <= b).All(x => x);
            if (sane) Array.Copy(loaded, RangeMs, RangeMs.Length);
            Core.Log.Msg($"[JudgmentBar] 판정 범위: {text}{(sane ? "" : "→ 비정상, 기본값 사용")}");
        }
        catch (Exception e) { Core.Log.Warning($"[JudgmentBar] JudgmentTimingMap 읽기 실패, 기본값 사용: {e.Message}"); }
    }

    private static Color JudgeColor(JudgmentType j) => j switch
    {
        JudgmentType.PerfectHigh => new Color(1f, 1f, 1f),
        JudgmentType.Perfect => new Color(0.35f, 0.75f, 1f),
        JudgmentType.Great => new Color(1f, 0.8f, 0.2f),
        JudgmentType.Good => new Color(0.4f, 0.9f, 0.4f),
        _ => new Color(1f, 0.3f, 0.4f),
    };

    private static string JudgeName(JudgmentType j) => j switch
    {
        JudgmentType.PerfectHigh => "PERFECT HIGH",
        JudgmentType.Perfect => "PERFECT",
        JudgmentType.Great => "GREAT",
        JudgmentType.Good => "GOOD",
        _ => j.ToString().ToUpperInvariant(),
    };

    private static void UpdateJudgmentBar()
    {
        if (!s_rangeLoaded)
        {
            s_rangeLoaded = true;
            if (!HookGuard.IsDisabled("PlayOverlay.LoadRanges"))
            {
                try { LoadRanges(); }
                catch (Exception e) { HookGuard.Fail("PlayOverlay.LoadRanges", e); }
            }
        }
        float now = Time.time;
        lock (s_lock)
        {
            foreach (var p in s_pending)
            {
                var h = p; h.Time = now;
                s_history.Add(h);
                s_last = h;
                s_hitVersion++;
                s_historyVersion++;
            }
            s_pending.Clear();
        }
        // 기록은 시간순으로 쌓이므로 만료된 것은 앞쪽에 몰려 있다 (예전 RemoveAll 람다는 매 프레임 클로저를 할당했다)
        int expired = 0;
        while (expired < s_history.Count && now - s_history[expired].Time > Duration) expired++;
        if (expired > 0)
        {
            s_history.RemoveRange(0, expired);
            s_historyVersion++;
        }

        float screenW = s_screenW, screenH = s_screenH;
        bool vertical = ModConfig.JudgmentBarVertical;
        int trackShape = ModConfig.JudgmentBarShape;
        int rangeShape = ModConfig.JudgmentBarRangeShape == -1 ? trackShape : ModConfig.JudgmentBarRangeShape;
        bool left = ModConfig.JudgmentBarSide.Equals("Left", StringComparison.OrdinalIgnoreCase);
        bool right = ModConfig.JudgmentBarSide.Equals("Right", StringComparison.OrdinalIgnoreCase);
        float barW = vertical ? 24f : 300f;
        float barH = vertical ? 300f : 24f;

        const float edge = 60f;
        float cx = left ? (vertical ? edge : edge + barW / 2f)
            : right ? (vertical ? screenW - edge : screenW - edge - barW / 2f)
            : (vertical ? edge : screenW / 2f);
        float cy = screenH / 2f;

        float maxMs = RangeMs[^1];
        float scale = (vertical ? barH / 2f : barW / 2f) / maxMs;

        var layout = (screenW, screenH, vertical, trackShape, rangeShape, ModConfig.JudgmentBarSide,
            RangeMs[0], RangeMs[1], RangeMs[2], RangeMs[3]);
        bool layoutChanged = !s_barLayoutValid || s_barLayout != layout;
        if (layoutChanged)
        {
            s_barLayout = layout;
            s_barLayoutValid = true;
            SetRect(s_track, new Rect(cx - barW / 2f, cy - barH / 2f, barW, barH), new Color(0.08f, 0.08f, 0.08f, 0.65f), trackShape);
            SetRange(s_ranges[0], cx, cy, barW, barH, vertical, RangeMs[2] * 2f * scale, new Color(0.65f, 0.55f, 0.12f, 0.18f), rangeShape); // GREAT
            SetRange(s_ranges[1], cx, cy, barW, barH, vertical, RangeMs[1] * 2f * scale, new Color(0.30f, 0.60f, 0.90f, 0.22f), rangeShape); // PERFECT
            SetRange(s_ranges[2], cx, cy, barW, barH, vertical, RangeMs[0] * 2f * scale, new Color(0.85f, 0.85f, 0.85f, 0.28f), rangeShape); // PERFECT HIGH
            SetRect(s_center, vertical ? new Rect(cx - barW / 2f - 2f, cy - 1f, barW + 4f, 2f) : new Rect(cx - 1f, cy - barH / 2f - 2f, 2f, barH + 4f), Color.white);
        }

        const int MaxVisibleTicks = 32;
        int activeCount = Math.Min(s_history.Count, MaxVisibleTicks);
        if (s_ticks.Count < activeCount)
        {
            while (s_ticks.Count < activeCount) s_ticks.Add(NewImage(s_barGroup, $"Tick{s_ticks.Count}"));
            s_label.transform.SetAsLastSibling(); // 새 틱이 글자를 덮지 않게
            s_ticksPlacedVersion = -1;
        }
        // 틱 위치는 판정이 들어오거나 만료되거나 배치가 바뀔 때만 다시 잡는다. 매 프레임은 투명도(색)만 바꾼다.
        bool placeTicks = layoutChanged || s_ticksPlacedVersion != s_historyVersion;
        s_ticksPlacedVersion = s_historyVersion;
        for (int i = 0; i < s_ticks.Count; i++)
        {
            var tick = s_ticks[i];
            if (i >= activeCount)
            {
                if (tick.enabled) tick.enabled = false;
                continue;
            }
            // 최근 틱 우선 표시 (오래된 틱은 자동 가려짐)
            var t = s_history[s_history.Count - activeCount + i];
            if (placeTicks)
            {
                float ms = Mathf.Clamp(t.OffsetMs, -maxMs, maxMs);
                Place(tick, vertical
                    ? new Rect(cx - barW / 2f - 1f, cy - ms * scale - 1f, barW + 2f, 2f)  // 위 = FAST
                    : new Rect(cx + ms * scale - 1f, cy - barH / 2f - 1f, 2f, barH + 2f)); // 오른쪽 = FAST
            }
            var c = JudgeColor(t.Judgment); c.a = Mathf.Clamp01(1f - (now - t.Time) / Duration) * 0.9f;
            tick.color = c; // 틱은 스프라이트 없는 사각형 (NewImage 기본값)
            if (!tick.enabled) tick.enabled = true;
        }

        if (s_last is HitTick last && now - last.Time < Duration)
        {
            // 글자·위치는 새 판정이 들어오거나 배치가 바뀔 때만 다시 설정한다. 매 프레임은 투명도만 바꾼다.
            if (s_labelVersion != s_hitVersion || layoutChanged)
            {
                s_labelVersion = s_hitVersion;
                string text = $"{(last.OffsetMs >= 0 ? "+" : "")}{last.OffsetMs:F1} ms · {JudgeName(last.Judgment)} ({(last.OffsetMs >= 0 ? "FAST" : "SLOW")})";
                bool labelLeft = vertical && right;
                const float labelW = 320f;
                var rect = vertical
                    ? (labelLeft ? new Rect(cx - barW / 2f - 10f - labelW, cy - 12f, labelW, 24f) : new Rect(cx + barW / 2f + 10f, cy - 12f, labelW, 24f))
                    : new Rect(cx - 90f, cy - barH / 2f - 28f, labelW, 24f);
                var align = labelLeft ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
                SetText(s_labelShadow, new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, align);
                SetText(s_label, rect, text, align);
            }
            float alpha = Mathf.Clamp01(1f - (now - last.Time) / Duration);
            s_labelShadow.color = new Color(0f, 0f, 0f, alpha * 0.8f);
            var c = JudgeColor(last.Judgment); c.a = alpha;
            s_label.color = c;
        }
        else if (s_label.enabled || s_labelShadow.enabled)
        {
            s_label.enabled = false;
            s_labelShadow.enabled = false;
            s_labelVersion = -1; // 다시 나타날 때 글자를 새로 설정
        }
    }

    private static void SetRange(Image img, float cx, float cy, float barW, float barH, bool vertical, float size, Color color, int shape)
    {
        SetRect(img, vertical
            ? new Rect(cx - barW / 2f + 1f, cy - size / 2f, barW - 2f, size)
            : new Rect(cx - size / 2f, cy - barH / 2f + 1f, size, barH - 2f), color, shape);
    }

    private static void SetText(TextMeshProUGUI t, Rect r, string text, TextAlignmentOptions align)
    {
        Place(t, r);
        t.text = text;
        t.alignment = align;
        t.enabled = true;
    }

    // ================= 키뷰어 =================

    [HarmonyPatch(typeof(PatternPlayer), "ProcessInputEvent")]
    private static class PatternPlayer_ProcessInputEvent_Patch
    {
        private const string Hook = "PatternPlayer.ProcessInputEvent";

        /// <summary>키 입력마다 게임 루프 스레드에서 불린다. 게임 멤버 접근은 HandleInput으로 격리 (HookGuard 설명 참고).</summary>
        private static void Prefix(PatternPlayer __instance, PatternPlayer.InputEvent input)
        {
            if (!InPlay || !ModConfig.EnableKeyViewer || __instance == null || HookGuard.IsDisabled(Hook)) return;
            try { HandleInput(__instance, input); }
            catch (Exception e)
            {
                if (!HookGuard.Fail(Hook, e)) WarnOnce($"[KeyViewer] 입력 처리 오류: {e.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining), Guarded("PatternPlayer.ProcessInputEvent")]
        private static void HandleInput(PatternPlayer player, PatternPlayer.InputEvent input)
        {
            var mode = player.LineMode;
            lock (s_keyLock)
            {
                if (s_laneOfKey == null || s_mode != mode)
                {
                    if (HookGuard.IsDisabled("PlayOverlay.BindKeys")) return;
                    try { BindKeys(mode); }
                    catch (Exception e)
                    {
                        HookGuard.Fail("PlayOverlay.BindKeys", e);
                        return;
                    }
                }
                if (s_laneOfKey.TryGetValue(input.KeyCode, out var index) && index < s_pressed.Length)
                    s_pressed[index] = input.IsPressed;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining), Guarded("PlayOverlay.BindKeys")]
    private static void BindKeys(LineModeType mode)
    {
        s_mode = mode;
        s_laneOfKey = new Dictionary<ushort, int>();
        var keys = InputStateMachine.GetKeyBindingArray(mode);
        int count = keys?.Length ?? 0;
        ushort pitch = InputStateMachine.GetPitchKey(mode);
        bool hasPitch = pitch != 0;
        s_labels = new string[count + (hasPitch ? 1 : 0)];
        s_pressed = new bool[s_labels.Length];
        for (int i = 0; i < count; i++)
        {
            s_laneOfKey[keys[i]] = i;
            s_labels[i] = KeyLabel(keys[i]);
        }
        s_pitchIndex = -1;
        if (hasPitch)
        {
            s_pitchIndex = count;
            s_laneOfKey[pitch] = count;
            s_labels[count] = KeyLabel(pitch);
        }
        var codes = new StringBuilder();
        for (int i = 0; i < count; i++) codes.Append($"0x{keys[i]:X4} ");
        Core.Log.Msg($"[KeyViewer] {mode} 키: {string.Join(" ", s_labels)} (코드 {codes}피치 0x{pitch:X4})");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameTextW(int lParam, StringBuilder lpString, int cchSize);

    /// <summary>RawKeycode → 키 이름. 상위 바이트 0xE0이면 확장키(방향키, 오른쪽 Ctrl 등), 하위 바이트 = 스캔코드.</summary>
    private static string KeyLabel(ushort code)
    {
        int scan = code & 0xFF;
        bool extended = (code >> 8) == 0xE0;
        var sb = new StringBuilder(32);
        int len = GetKeyNameTextW((scan << 16) | (extended ? 1 << 24 : 0), sb, sb.Capacity);
        var name = len > 0 ? sb.ToString() : $"{code:X}";
        return name switch
        {
            "Space" or "스페이스" => "SPC",
            "Left Shift" or "Shift" => "SFT",
            "Right Shift" => "RSFT",
            "Ctrl" or "Left Ctrl" => "CTL",
            "Right Ctrl" => "RCTL",
            "Alt" => "ALT",
            "Enter" => "ENT",
            "Backspace" => "BS",
            _ => name.Length <= 4 ? name.ToUpperInvariant() : name[..4].ToUpperInvariant(),
        };
    }

    private static void UpdateKeyViewer()
    {
        string[] labels; int pitchIndex; int len;
        lock (s_keyLock)
        {
            labels = s_labels;
            pitchIndex = s_pitchIndex;
            len = s_pressed.Length;
            if (s_pressedBuffer.Length != len) s_pressedBuffer = new bool[len];
            Array.Copy(s_pressed, s_pressedBuffer, len);
        }

        while (s_keys.Count < labels.Length)
        {
            var bg = NewImage(s_keyGroup, $"Key{s_keys.Count}");
            var border = bg.gameObject.AddComponent<Outline>();
            border.effectDistance = new Vector2(1f, -1f);
            var label = NewText(bg.transform, "Label", 15);
            var rt = label.rectTransform; // 글자는 칸 전체를 채운다 (한 번만 설정)
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            label.alignment = TextAlignmentOptions.Center;
            s_keys.Add((bg, border, label));
            s_keyLayoutValid = false;
        }

        const float boxW = 44f, boxH = 44f, gap = 6f, bottom = 56f;
        float pitchW = boxW * 2f + gap;

        // 칸 배치·글자는 화면 크기나 키 배열(라인 모드)이 바뀔 때만 다시 설정한다
        var layout = (s_screenW, s_screenH, labels, pitchIndex);
        if (!s_keyLayoutValid || s_keyLayout != layout)
        {
            s_keyLayout = layout;
            s_keyLayoutValid = true;
            if (s_keyDrawn.Length != s_keys.Count) s_keyDrawn = new int[s_keys.Count];
            Array.Fill(s_keyDrawn, -1);
            float totalW = 0;
            for (int i = 0; i < labels.Length; i++) totalW += (i == pitchIndex ? pitchW : boxW) + (i > 0 ? gap : 0);
            float x = (layout.Item1 - totalW) / 2f;
            float y = layout.Item2 - bottom - boxH;
            for (int i = 0; i < s_keys.Count; i++)
            {
                var (bg, _, text) = s_keys[i];
                if (i >= labels.Length) { bg.gameObject.SetActive(false); continue; }
                bg.gameObject.SetActive(true);
                float w = i == pitchIndex ? pitchW : boxW;
                Place(bg, new Rect(x, y, w, boxH));
                bg.sprite = null;
                bg.enabled = true;
                text.text = labels[i];
                x += w + gap;
            }
        }

        // 누름 상태는 바뀐 칸만 색을 다시 칠한다
        for (int i = 0; i < labels.Length && i < s_keys.Count; i++)
        {
            int state = i < len && s_pressedBuffer[i] ? 1 : 0;
            if (s_keyDrawn[i] == state) continue;
            s_keyDrawn[i] = state;
            var (bg, border, text) = s_keys[i];
            bool down = state == 1;
            bg.color = down ? (i == pitchIndex ? ModConfig.KeyViewerPitchPressedColor : ModConfig.KeyViewerPressedColor) : ModConfig.KeyViewerNormalColor;
            border.effectColor = down ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.35f);
            text.color = down ? new Color(0.05f, 0.05f, 0.05f, 1f) : new Color(1f, 1f, 1f, 0.9f);
        }
    }

    // ================= 모양 스프라이트 =================

    /// <summary>shape: 1 = 알약 캡슐, 2 = 삼각/다이아몬드. 크기별로 캐시.</summary>
    private static Sprite ShapeSprite(int shape, int w, int h)
    {
        if (w < 2 || h < 2) return null;
        if (s_shapeSprites.TryGetValue((shape, w, h), out var cached) && cached != null) return cached;
        if (s_shapeSprites.Count > 64)
        {
            // 판정바 틀·범위가 쓰던 스프라이트도 파괴되므로 다음 프레임에 다시 설정하게 한다
            s_barLayoutValid = false;
            foreach (var kvp in s_shapeSprites)
            {
                if (kvp.Value != null)
                {
                    if (kvp.Value.texture != null) UnityEngine.Object.Destroy(kvp.Value.texture);
                    UnityEngine.Object.Destroy(kvp.Value);
                }
            }
            s_shapeSprites.Clear();
        }
        try
        {
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                int rowOffset = y * w;
                for (int x = 0; x < w; x++)
                {
                    float d = shape == 1 ? Capsule(x + 0.5f, y + 0.5f, w, h) : Diamond(x + 0.5f, y + 0.5f, w, h);
                    pixels[rowOffset + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d));
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            s_shapeSprites[(shape, w, h)] = sprite;
            return sprite;
        }
        catch (Exception e)
        {
            WarnOnce($"[JudgmentBar] 모양 텍스처 생성 실패, 사각으로 그림: {e.Message}");
            return null;
        }
    }

    /// <summary>양끝이 반원인 알약 모양까지의 부호 있는 거리.</summary>
    private static float Capsule(float px, float py, int w, int h)
    {
        float r = Mathf.Min(w, h) / 2f;
        float ax = w >= h ? r : w / 2f, ay = w >= h ? h / 2f : r;
        float bx = w >= h ? w - r : w / 2f, by = w >= h ? h / 2f : h - r;
        float dx = bx - ax, dy = by - ay, lenSq = dx * dx + dy * dy;
        float t = lenSq > 0.0001f ? Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / lenSq) : 0f;
        float ex = px - (ax + t * dx), ey = py - (ay + t * dy);
        return Mathf.Sqrt(ex * ex + ey * ey) - r;
    }

    /// <summary>가운데가 가장 넓고 양끝이 뾰족한 다이아몬드까지의 부호 있는 거리.</summary>
    private static float Diamond(float px, float py, int w, int h)
    {
        float hw = w / 2f, hh = h / 2f;
        return (hh * Mathf.Abs(px - hw) + hw * Mathf.Abs(py - hh) - hw * hh) / Mathf.Sqrt(hw * hw + hh * hh);
    }
}
