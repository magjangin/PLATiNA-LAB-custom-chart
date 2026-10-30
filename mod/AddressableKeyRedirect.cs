using HarmonyLib;
using Il2CppInterop.Runtime;

namespace PLATiNALAB.Mod;

/// <summary>
/// 커스텀 곡의 리소스(차트 .ppp 등) 주소는 게임이 SongInfo.Id로 만들기 때문에 "custom_test"가 들어간 키가 된다.
/// Addressables의 IResourceLocator.Locate(object key, ...) 구현체들에 Prefix를 걸어
/// 문자열 키 속 커스텀 곡 ID를 기반 곡 ID로 바꿔 원본 리소스를 찾게 한다.
/// PlayDirector.LoadGame은 LoadTargetPattern 안에 인라인돼 Harmony로 잡히지 않으므로(실측) 이 지점을 쓴다.
/// Locate는 인터페이스 경유 호출이라 인라인되지 않는다.
/// </summary>
internal static class AddressableKeyRedirect
{
    public static void Apply(HarmonyLib.Harmony harmony)
    {
        var prefix = new HarmonyMethod(typeof(AddressableKeyRedirect), nameof(Prefix));
        int count = 0;
        // 인터롭 어셈블리는 지연 로드되므로 OnInitializeMelon 시점엔 명시적으로 로드해야 한다
        try { System.Reflection.Assembly.Load("Unity.Addressables"); }
        catch (Exception e) { Core.Log.Warning($"[KeyRedirect] Unity.Addressables 로드 실패: {e.Message}"); }
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name != "Unity.Addressables") continue;
            Type[] types;
            try { types = asm.GetTypes(); } catch { continue; }
            foreach (var t in types)
            {
                // Il2CppInterop은 IL2CPP 인터페이스를 C# 인터페이스로 남기지 않으므로 메서드 이름/시그니처로 찾는다
                if (t.IsInterface || t.IsAbstract || t.Name == "IResourceLocator") continue;
                var m = t.GetMethods().FirstOrDefault(x =>
                    x.Name == "Locate" && !x.IsAbstract && x.DeclaringType == t && x.GetParameters().Length == 3);
                if (m == null) continue;
                try
                {
                    harmony.Patch(m, prefix);
                    count++;
                    Core.Log.Msg($"[KeyRedirect] 패치: {t.FullName}.Locate");
                }
                catch (Exception e) { Core.Log.Warning($"[KeyRedirect] {t.FullName}.Locate 패치 실패: {e.Message}"); }
            }
        }
        if (count == 0) Core.Log.Warning("[KeyRedirect] IResourceLocator 구현체를 찾지 못함");
    }

    private static void Prefix(ref Il2CppSystem.Object key)
    {
        if (key == null || CustomSongs.s_baseSong == null) return;
        try
        {
            if (IL2CPP.il2cpp_object_get_class(key.Pointer) != Il2CppClassPointerStore<string>.NativeClassPtr) return;
            var s = IL2CPP.Il2CppStringToManaged(key.Pointer);
            if (s == null || !s.Contains(CustomSongs.IdPrefix)) return;
            var song = CustomSongs.All.FirstOrDefault(c => s.Contains(c.Id));
            if (song == null) return;
            var redirected = s.Replace(song.Id, CustomSongs.s_baseSong.Id);
            key = new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(redirected));
            Core.Log.Msg($"[KeyRedirect] '{s}' -> '{redirected}'");
        }
        catch (Exception e) { Core.Log.Warning($"[KeyRedirect] 실패: {e.Message}"); }
    }
}
