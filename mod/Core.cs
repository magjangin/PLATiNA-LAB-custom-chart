using MelonLoader;
using PLATiNALAB.Mod;

[assembly: MelonInfo(typeof(Core), "PLATiNA LAB Custom Mod", "1.0.0", "Hwa-young-wang")]
[assembly: MelonGame(null, null)]

namespace PLATiNALAB.Mod;

public class Core : MelonMod
{
    public static MelonLogger.Instance Log { get; private set; }
    public static HarmonyLib.Harmony Patcher { get; private set; }

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        Patcher = HarmonyInstance;
        Log.Msg("==================================================");
        Log.Msg("[PLATiNALAB.Mod] PLATiNA LAB Mod Skeleton Loaded!");
        Log.Msg("==================================================");
        // 초기화 단계는 서로 독립 — 한 단계가 실패해도(파일 손상·게임 업데이트) 나머지(특히 일반 곡 플레이에 쓰는 훅)는 계속 살린다.
        // 실패의 영향은 ModHealth가 점검한다 (예: Locate 패치가 없으면 커스텀 곡 주입을 끔).
        Step("설정 로드", () => ModConfig.Load("시작"));
        Step("차트 초기화", CustomCharts.Init);
        Step("커스텀 곡 스캔", CustomSongs.Scan);
        Step("Addressables 키 치환 패치", () => AddressableKeyRedirect.Apply(HarmonyInstance));
    }

    public override void OnLateInitializeMelon()
    {
        try { ModHealth.CheckOnStart(); }
        catch (Exception e) { Log.Error($"[Health] 시작 점검 실패 (모드는 계속 동작): {e}"); }
    }

    private static void Step(string name, Action action)
    {
        try { action(); }
        catch (Exception e) { Log.Error($"[Init] {name} 실패 — 이 단계만 건너뜁니다: {e}"); }
    }

    public override void OnUpdate() => PlayOverlay.Update();
}
