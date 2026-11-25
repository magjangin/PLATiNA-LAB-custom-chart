using System.Reflection;
using System.Reflection.Emit;

/// <summary>
/// [Offline] 오프라인 전용 패치의 게이트 검사 (19차 감사).
/// decf3b7에서 스팀·로그인·서버 우회가 게이트 없이 모든 사용자에게 걸려, 스팀으로 정상 실행해도 공식 곡 기록이 서버로 가지 않고
/// 게임 메뉴의 종료가 막혔다. 같은 실수를 다시 하지 않도록 빌드된 DLL에서 확인한다.
///   1. [OfflineOnly] 클래스의 Prefix/Postfix는 IL에서 Offline.get_Enabled 또는 Offline.ShouldBlockQuit를 직접 호출해야 한다.
///   2. Steamworks·HighendGames.GamePlatform 타입을 겨냥한 [HarmonyPatch] 클래스는 [OfflineOnly]여야 한다.
/// </summary>
static class OfflineCheck
{
    static readonly Dictionary<short, OpCode> s_ops =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);

    static readonly string[] s_platformAssemblies = { "Il2Cppcom.rlabrecque.steamworks.net", "Il2CppHighendGames.GamePlatform" };

    public static int Run(Assembly modAsm)
    {
        int checkedClasses = 0, failed = 0;
        foreach (var type in SafeTypes(modAsm))
        {
            bool offlineOnly = type.GetCustomAttributesData().Any(d => d.AttributeType.Name == "OfflineOnlyAttribute");
            var target = PatchTargetType(type);
            if (target != null && !offlineOnly && s_platformAssemblies.Any(a => target.Assembly.GetName().Name?.StartsWith(a, StringComparison.Ordinal) == true))
            {
                failed++;
                Console.WriteLine($"   [FAIL Offline] {type.FullName}: 스팀/플랫폼 타입 {target.FullName} 패치에 [OfflineOnly]가 없음 — 온라인 사용자에게도 우회가 걸린다");
            }
            if (!offlineOnly) continue;
            checkedClasses++;
            var hooks = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.Name is "Prefix" or "Postfix").ToList();
            if (hooks.Count == 0)
            {
                failed++;
                Console.WriteLine($"   [FAIL Offline] {type.FullName}: [OfflineOnly]인데 Prefix/Postfix가 없음");
            }
            foreach (var m in hooks)
                if (!CallsOfflineGate(m))
                {
                    failed++;
                    Console.WriteLine($"   [FAIL Offline] {type.FullName}.{m.Name}: Offline.Enabled/ShouldBlockQuit를 확인하지 않음 — OfflineMode=0에서도 우회가 걸린다");
                }
        }
        Console.WriteLine($"[Offline] 오프라인 전용 패치 게이트 검사: [OfflineOnly] 클래스 {checkedClasses}개 중 실패 {failed}개");
        return checkedClasses > 0 ? failed : failed + 1;
    }

    static Type PatchTargetType(Type patchClass)
    {
        foreach (var d in patchClass.GetCustomAttributesData())
        {
            if (d.AttributeType.Name is not ("HarmonyPatch" or "HarmonyPatchAttribute")) continue;
            foreach (var arg in d.ConstructorArguments)
                if (arg.Value is Type t) return t;
        }
        return null;
    }

    static bool CallsOfflineGate(MethodInfo m)
    {
        byte[] il;
        try { il = m.GetMethodBody()?.GetILAsByteArray(); } catch { return false; }
        if (il == null) return false;
        int i = 0;
        while (i < il.Length)
        {
            short value = il[i] == 0xFE && i + 1 < il.Length ? (short)(0xFE00 | il[i + 1]) : il[i];
            if (!s_ops.TryGetValue(value, out var op)) return false;
            i += op.Size;
            switch (op.OperandType)
            {
                case OperandType.InlineMethod:
                {
                    try
                    {
                        var member = m.Module.ResolveMethod(BitConverter.ToInt32(il, i));
                        if (member?.DeclaringType?.FullName == "PLATiNALAB.Mod.Offline" && member.Name is "get_Enabled" or "ShouldBlockQuit")
                            return true;
                    }
                    catch { }
                    i += 4;
                    break;
                }
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: i += 1; break;
                case OperandType.InlineVar: i += 2; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: i += 8; break;
                case OperandType.InlineSwitch: i += 4 + BitConverter.ToInt32(il, i) * 4; break;
                default: i += 4; break;
            }
        }
        return false;
    }

    static IEnumerable<Type> SafeTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
    }
}
