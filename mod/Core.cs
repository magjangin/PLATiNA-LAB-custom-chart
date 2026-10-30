using MelonLoader;
using PLATiNALAB.Mod;

[assembly: MelonInfo(typeof(Core), "PLATiNA LAB Custom Mod", "1.0.0", "Hwa-young-wang")]
[assembly: MelonGame(null, null)]

namespace PLATiNALAB.Mod;

public class Core : MelonMod
{
    public static MelonLogger.Instance Log { get; private set; }

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        Log.Msg("==================================================");
        Log.Msg("[PLATiNALAB.Mod] PLATiNA LAB Mod Skeleton Loaded!");
        Log.Msg("==================================================");
        CustomCharts.Init();
        CustomSongs.Scan();
        AddressableKeyRedirect.Apply(HarmonyInstance);
    }}
