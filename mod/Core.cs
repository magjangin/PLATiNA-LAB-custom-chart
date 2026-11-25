using MelonLoader;
using PLATiNALAB.Mod;

[assembly: MelonInfo(typeof(Core), "PLATiNA LAB Custom Mod", "1.4.0", "Hwa-young-wang")]
[assembly: MelonGame("HIGHEND GAMES", "PLATiNA  LAB")]

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
        Log.Msg($"[PLATiNALAB.Mod] PLATiNA LAB Custom Mod v{Info.Version} Loaded!");
        Log.Msg("==================================================");
        // 초기화 단계는 서로 독립 — 한 단계가 실패해도(파일 손상·게임 업데이트) 나머지(특히 일반 곡 플레이에 쓰는 훅)는 계속 살린다.
        // 실패의 영향은 ModHealth가 점검한다 (예: Locate 패치가 없으면 커스텀 곡 주입을 끔).
        Step("설정 로드", () => ModConfig.Load("시작"));
        // 스팀 SDK 초기화보다 먼저 정해야 한다 (SteamBypassPatches가 이 값으로 우회 여부를 판단)
        Step("오프라인 모드", Offline.Init);
        // 자동 PatchAll은 끄고([assembly: HarmonyDontPatchAll], SafePatcher.cs) 클래스마다 격리해서 적용한다
        Step("Harmony 패치 적용", () => SafePatcher.ApplyAll(HarmonyInstance, typeof(Core).Assembly));
        Step("진단 패치", () => { if (ModConfig.DiagnosticLogs) DiagnosticPatches.Apply(HarmonyInstance); });
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

    public override void OnUpdate()
    {
        PlayOverlay.Update();
        SongOwnership.Tick();
    }

    public override void OnApplicationQuit()
    {
        try { SongOwnership.Flush(); }
        catch (Exception e) { Log.Warning($"[Ownership] 종료 시 저장 실패: {e.Message}"); }
    }
}
