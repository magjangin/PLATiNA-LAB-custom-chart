// 오프라인 인터롭 하네스 — 게임을 실행하지 않고 MelonLoader가 만든 인터롭 어셈블리로 검증한다.
//   dotnet run -c Release            : InteropNames 신호별 누락·오탐, 람다 메서드 판별, Locate 엄격 조건 후보 수, 모드 [Guarded]/[HarmonyPatch]/[Required]/[Layout]/[Body]/[BmsFind] 검증 (종료 코드 0 = 통과)
//   dotnet run -c Release -- explore : 컴파일러 생성 타입의 멤버(프로퍼티) 이름 빈도 (멤버 모양 신호 조사용)
// 정답 기준: 인터롭 [ObfuscatedName]의 원래 이름(마지막 중첩 이름)에 '<'가 있거나, 이름에 '<'가 그대로 남은 타입.
// 게임 경로는 환경 변수 PLATINALAB_GAME, 모드 DLL 경로는 PLATINALAB_MOD로 바꿀 수 있다.
global using System.Runtime.CompilerServices;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using PLATiNALAB.Mod;

namespace PLATiNALAB.Mod { internal static class Core { internal static class Log { public static void Warning(string s) => Console.WriteLine("W " + s); public static void Msg(string s) => Console.WriteLine(s); } } }

static class P
{
    static readonly string Game = Environment.GetEnvironmentVariable("PLATINALAB_GAME") ?? @"H:\steam\steamapps\common\PLATiNA LAB";
    static readonly string Interop = Path.Combine(Game, "MelonLoader", "Il2CppAssemblies");
    static readonly string Net6 = Path.Combine(Game, "MelonLoader", "net6");
    static int Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            foreach (var dir in new[] { Interop, Net6 })
            {
                var p = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
            }
            return null;
        };
        var asms = Directory.GetFiles(Interop, "*.dll").Select(f => { try { return AssemblyLoadContext.Default.LoadFromAssemblyPath(f); } catch { return null; } }).Where(a => a != null).ToList();
        if (args.Length > 0 && args[0] == "explore") { Explore(asms); return 0; }
        Evaluate(asms);
        bool textOk = VerifyTextSanitizer();
        return VerifyModAssembly() && textOk ? 0 : 1;
    }

    /// <summary>곡 제목·아티스트 정규화 회귀 검사 (따옴표를 축약형으로 오인해 붙이던 문제 포함).</summary>
    static bool VerifyTextSanitizer()
    {
        var cases = new (string input, string expected)[]
        {
            ("you'  re", "you're"),
            ("don ' t", "don't"),
            ("it ' s", "it's"),
            ("o ' clock", "o'clock"),
            ("Don’t Stop", "Don't Stop"),
            ("Rock 'n' Roll", "Rock 'n' Roll"),
            ("He said 'hello' world", "He said 'hello' world"),
            ("the 'S' rank", "the 'S' rank"),
            ("  double   space  ", "double space"),
            ("どりーむもーど", "どりーむもーど"),
        };
        int failed = 0;
        foreach (var (input, expected) in cases)
        {
            var actual = TextSanitizer.Sanitize(input);
            if (actual == expected) continue;
            failed++;
            Console.WriteLine($"   [FAIL TextSanitizer] '{input}' -> '{actual}' (기대 '{expected}')");
        }
        Console.WriteLine($"[TextSanitizer] {cases.Length}건 중 실패 {failed}건");
        return failed == 0;
    }

    static string Last(string original) { int plus = original.LastIndexOf('+'); return plus >= 0 ? original[(plus + 1)..] : original; }

    static void Explore(List<Assembly> asms)
    {
        var memberNames = new Dictionary<string, int>();
        int shown = 0;
        foreach (var a in asms)
        foreach (var t in InteropNames.SafeGetTypes(a))
        {
            var orig = InteropNames.OriginalName(t);
            if (orig == null || Last(orig).IndexOf('<') < 0) continue;
            var names = new List<string>();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                names.Add("P:" + p.Name + "=" + (InteropNames.OriginalName(p) ?? ""));
                var key = Regex.Replace(p.Name, @"\d+", "N");
                memberNames[key] = memberNames.GetValueOrDefault(key) + 1;
            }
            if (shown++ < 12) Console.WriteLine($"{t.FullName}  [{Last(orig)}]\n   " + string.Join(" ", names.Take(12)));
        }
        Console.WriteLine("--- 컴파일러 생성 타입의 프로퍼티 이름 빈도 (숫자=N)");
        foreach (var kv in memberNames.OrderByDescending(k => k.Value).Take(40)) Console.WriteLine($"{kv.Value,6} {kv.Key}");
    }

    static void Evaluate(List<Assembly> asms)
    {
        var sets = new (string, InteropNames.Signals)[] {
            ("이름 패턴", InteropNames.Signals.NamePattern),
            ("멤버 모양", InteropNames.Signals.MemberShape),
            ("패턴+모양", InteropNames.Signals.NamePattern | InteropNames.Signals.MemberShape),
            ("전체", InteropNames.Signals.All),
        };
        var types = asms.SelectMany(a => InteropNames.SafeGetTypes(a)).ToList();
        int positives = 0, total = types.Count;
        var kinds = new SortedDictionary<string, int>();
        var miss = sets.ToDictionary(s => s.Item1, s => new List<string>());
        var fp = sets.ToDictionary(s => s.Item1, s => new List<string>());
        foreach (var t in types)
        {
            var orig = InteropNames.OriginalName(t);
            bool truth = (orig != null && Last(orig).IndexOf('<') >= 0) || t.Name.IndexOf('<') >= 0 || t.Name.StartsWith("__StaticArrayInitTypeSize");
            if (truth)
            {
                var o = Last(orig ?? t.Name);
                var kind = o.StartsWith("<>c__DisplayClass") ? "클로저" : o == "<>c" || o.StartsWith("<>c__") || o.StartsWith("<>c`") ? "람다 캐시"
                    : o.EndsWith(">d") || Regex.IsMatch(o, @">d__\d+") ? "상태 머신" : "기타";
                bool shape = InteropNames.ReasonSelf(t, InteropNames.Signals.MemberShape) != InteropNames.Signals.None;
                var k = kind + (shape ? " O" : " X");
                kinds[k] = kinds.GetValueOrDefault(k) + 1;
            }
            if (truth) positives++;
            foreach (var (label, sig) in sets)
            {
                bool got = InteropNames.ReasonSelf(t, sig) != InteropNames.Signals.None;
                if (truth && !got) miss[label].Add($"{t.FullName} [{Last(orig ?? t.Name)}]");
                if (!truth && got && !t.Name.StartsWith("MethodInfoStoreGeneric_")) fp[label].Add(t.FullName + " <" + InteropNames.ReasonSelf(t, sig) + ">");
            }
        }
        Console.WriteLine("멤버 모양 신호 종류별(O=판별): " + string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value}")));
        Console.WriteLine($"어셈블리 {asms.Count}개, 타입 {total}개, 컴파일러 생성 타입(정답) {positives}개");
        foreach (var (label, _) in sets)
        {
            Console.WriteLine($"[{label}] 누락 {miss[label].Count}, 오탐 {fp[label].Count}");
            foreach (var m in miss[label].Take(6)) Console.WriteLine("   누락 " + m);
            foreach (var m in fp[label].Take(12)) Console.WriteLine("   오탐 " + m);
        }

        // 람다 본문 메서드 (Unity.Addressables, Il2CppPlatinaLab.Main): 이름에 _b__/_g__가 있는 메서드가 모두 판별되는지
        foreach (var an in new[] { "Unity.Addressables", "Il2CppPlatinaLab.Main" })
        {
            var a = asms.First(x => x.GetName().Name == an);
            int lambdas = 0, caught = 0;
            foreach (var t in InteropNames.SafeGetTypes(a))
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!Regex.IsMatch(m.Name, "_[bg]__")) continue;
                lambdas++;
                if (InteropNames.IsCompilerGenerated(m)) caught++;
            }
            Console.WriteLine($"{an}: 람다/로컬 함수 이름 메서드 {lambdas}개 중 {caught}개 판별");
        }

        // AddressableKeyRedirect 엄격 조건 재현
        var found = new List<string>();
        int skipped = 0;
        foreach (var a in asms.Where(x => x.GetName().Name.IndexOf("Addressables", StringComparison.OrdinalIgnoreCase) >= 0))
        foreach (var t in InteropNames.SafeGetTypes(a))
        {
            if (t.IsInterface || t.ContainsGenericParameters) continue;
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.Name != "Locate" || m.IsAbstract || m.ContainsGenericParameters) continue;
                if (InteropNames.IsCompilerGenerated(m)) { skipped++; continue; }
                if (InteropNames.IsIl2CppInterfaceOrAbstract(m)) continue;
                var ps = m.GetParameters();
                if (ps.Length == 3 && ps[0].ParameterType.FullName == "Il2CppSystem.Object" && m.ReturnType == typeof(bool) && ps[1].ParameterType.Name == "Type" && ps[2].ParameterType.IsByRef)
                    found.Add(t.FullName);
            }
        }
        Console.WriteLine($"Locate 엄격 조건 {found.Count}건 (컴파일러 생성 제외 {skipped}): {string.Join(", ", found)}");
    }

    static string FindModDll()
    {
        var env = Environment.GetEnvironmentVariable("PLATINALAB_MOD");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var candidates = new[]
        {
            Path.Combine(repoRoot, "mod", "bin", "Release", "net6.0", "PLATiNALAB.Mod.dll"),
            Path.Combine(repoRoot, "mod", "bin", "Debug", "net6.0", "PLATiNALAB.Mod.dll"),
            Path.Combine(Game, "Mods", "PLATiNALAB.Mod.dll"),
        };
        return candidates.Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    static bool VerifyModAssembly()
    {
        var modPath = FindModDll();
        if (modPath == null)
        {
            Console.WriteLine("모드 DLL 없음 — [Guarded]/[HarmonyPatch] 검증 건너뜀");
            return true;
        }
        Console.WriteLine($"모드 검증 대상: {modPath}");
        var modAsm = AssemblyLoadContext.Default.LoadFromAssemblyPath(modPath);

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        int guardedCount = 0, guardedFailed = 0;
        int patchCount = 0, patchFailed = 0;

        foreach (var type in InteropNames.SafeGetTypes(modAsm))
        {
            foreach (var data in type.GetCustomAttributesData())
            {
                if (data.AttributeType.Name != "HarmonyPatch" && data.AttributeType.Name != "HarmonyPatchAttribute") continue;
                patchCount++;
                if (!VerifyHarmonyPatch(type, data, out var error))
                {
                    patchFailed++;
                    Console.WriteLine($"   [FAIL HarmonyPatch] {type.FullName}: {error}");
                }
            }

            MethodInfo[] methods;
            try { methods = type.GetMethods(flags); } catch { continue; }
            foreach (var m in methods)
            {
                string hook = null;
                foreach (var data in m.GetCustomAttributesData())
                {
                    if (data.AttributeType.Name == "GuardedAttribute" && data.ConstructorArguments.Count > 0)
                    {
                        hook = data.ConstructorArguments[0].Value as string;
                        break;
                    }
                }
                if (hook == null || m.ContainsGenericParameters) continue;
                guardedCount++;
                try { RuntimeHelpers.PrepareMethod(m.MethodHandle); }
                catch (Exception e)
                {
                    guardedFailed++;
                    Console.WriteLine($"   [FAIL Guarded] {type.FullName}.{m.Name} ({hook}): {e.GetType().Name}: {e.Message}");
                }
            }
        }

        Console.WriteLine($"[Guarded] 사전 JIT 검증: {guardedCount}개 중 실패 {guardedFailed}개");
        Console.WriteLine($"[HarmonyPatch] 타깃 존재 검증: {patchCount}개 중 실패 {patchFailed}개");

        // ModHealth.s_requiredPatches의 모든 이름이 실제 [HarmonyPatch] 타깃과 일치하는지 (오타·리팩토링으로 어긋나면
        // 게임에서 항상 "필수 패치 누락"으로 커스텀 곡이 꺼지므로 오프라인에서 먼저 잡는다)
        int requiredFailed = 0;
        var healthType = modAsm.GetType("PLATiNALAB.Mod.ModHealth");
        var required = healthType?.GetField("s_requiredPatches", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as string[];
        if (required == null)
        {
            requiredFailed++;
            Console.WriteLine("   [FAIL Required] ModHealth.s_requiredPatches를 찾지 못함");
        }
        else
        {
            foreach (var name in required)
                if (!PatchedKeys.Contains(name))
                {
                    requiredFailed++;
                    Console.WriteLine($"   [FAIL Required] 필수 패치 '{name}'에 대응하는 [HarmonyPatch]가 없음");
                }
            Console.WriteLine($"[Required] 필수 패치 이름 대조: {required.Length}개 중 실패 {requiredFailed}개");
        }
        int layoutFailed = VerifyFieldLayout(modAsm);
        int bodyFailed = VerifyMethodBodies(modAsm);
        int bmsFailed = VerifyBmsFindFor(modAsm);
        return guardedCount > 0 && patchCount > 0 && guardedFailed == 0 && patchFailed == 0 && requiredFailed == 0 && layoutFailed == 0 && bodyFailed == 0 && bmsFailed == 0;
    }

    /// <summary>
    /// [BmsFind] 난이도별 BMS 선택 회귀 (BmsConverter.FindFor를 빈 임시 폴더에서 리플렉션으로 호출, 게임 의존 없음).
    /// 예전 부분 문자열 매칭은 over의 별칭 "ex"가 "next_hard.bms"에 걸려 hard 차트를 over로 썼다.
    /// </summary>
    static int VerifyBmsFindFor(Assembly modAsm)
    {
        var find = modAsm.GetType("PLATiNALAB.Mod.BmsConverter")?.GetMethod("FindFor", BindingFlags.Public | BindingFlags.Static);
        if (find == null) { Console.WriteLine("   [FAIL BmsFind] BmsConverter.FindFor 없음"); return 1; }
        var cases = new (string[] files, string difficulty, string expected)[]
        {
            (new[] { "hwa2.bms" }, "easy", "hwa2.bms"),                                   // 이름 매칭 없음 → 첫 BMS
            (new[] { "next_hard.bms", "song_over.bms" }, "over", "song_over.bms"),        // "ex"가 next에 걸리면 안 됨
            (new[] { "space.bms", "song_sp.bms" }, "plus", "song_sp.bms"),                // "sp"가 space에 걸리면 안 됨
            (new[] { "a_ez.bms", "b_easy.bms" }, "easy", "b_easy.bms"),                   // 정확한 이름이 별칭보다 우선
            (new[] { "song[EZ].bms", "song[HD].bms" }, "hard", "song[HD].bms"),
            (new[] { "song_hard2.bme", "song_easy2.bme" }, "hard", "song_hard2.bme"),     // 숫자에서 낱말을 끊음
            (new[] { "曲_简单.bms", "曲_困难.bms" }, "hard", "曲_困难.bms"),                 // 중국어 별칭은 부분 문자열
            (new[] { "곡 하드.bms", "곡 이지.bms" }, "easy", "곡 이지.bms"),
        };
        int failed = 0;
        foreach (var (files, difficulty, expected) in cases)
        {
            var dir = Path.Combine(Path.GetTempPath(), "platinalab-harness-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "");
                var actual = find.Invoke(null, new object[] { dir, difficulty }) as string;
                if (Path.GetFileName(actual ?? "") == expected) continue;
                failed++;
                Console.WriteLine($"   [FAIL BmsFind] {difficulty} in [{string.Join(", ", files)}] → '{Path.GetFileName(actual ?? "")}' (기대 '{expected}')");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
        Console.WriteLine($"[BmsFind] 난이도별 BMS 선택 {cases.Length}건 중 실패 {failed}건");
        return failed;
    }

    // ---------------- IL 검사: 훅 본문 직접 참조 / 같은 메서드 try ----------------

    static readonly Dictionary<short, System.Reflection.Emit.OpCode> OpCodesByValue =
        typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)).ToDictionary(o => o.Value);

    /// <summary>메서드 IL이 직접 참조하는 게임(인터롭) 타입·멤버 이름. 상수(enum 값)는 IL에 숫자로 들어가므로 잡히지 않는다(= 타입 로드 없음).</summary>
    static List<string> GameReferences(MethodBase m)
    {
        var result = new List<string>();
        byte[] il;
        try { il = m.GetMethodBody()?.GetILAsByteArray(); } catch { return result; }
        if (il == null) return result;
        Type[] typeArgs = m.DeclaringType?.IsGenericType == true ? m.DeclaringType.GetGenericArguments() : null;
        Type[] methodArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;
        int i = 0;
        while (i < il.Length)
        {
            short value = il[i] == 0xFE && i + 1 < il.Length ? (short)(0xFE00 | il[i + 1]) : il[i];
            if (!OpCodesByValue.TryGetValue(value, out var op)) break;
            i += op.Size;
            switch (op.OperandType)
            {
                case System.Reflection.Emit.OperandType.InlineMethod:
                case System.Reflection.Emit.OperandType.InlineField:
                case System.Reflection.Emit.OperandType.InlineType:
                case System.Reflection.Emit.OperandType.InlineTok:
                {
                    int token = BitConverter.ToInt32(il, i);
                    try
                    {
                        var member = m.Module.ResolveMember(token, typeArgs, methodArgs);
                        var owner = member as Type ?? member.DeclaringType;
                        if (owner != null && MentionsGameType(owner, new HashSet<Type>()))
                            result.Add(member is Type ? owner.FullName : $"{owner.Name}.{member.Name}");
                    }
                    catch { }
                    i += 4;
                    break;
                }
                case System.Reflection.Emit.OperandType.InlineNone: break;
                case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                case System.Reflection.Emit.OperandType.ShortInlineI:
                case System.Reflection.Emit.OperandType.ShortInlineVar: i += 1; break;
                case System.Reflection.Emit.OperandType.InlineVar: i += 2; break;
                case System.Reflection.Emit.OperandType.InlineI8:
                case System.Reflection.Emit.OperandType.InlineR: i += 8; break;
                case System.Reflection.Emit.OperandType.InlineSwitch: i += 4 + BitConverter.ToInt32(il, i) * 4; break;
                default: i += 4; break;
            }
        }
        return result.Distinct().ToList();
    }

    static bool MentionsGameType(Type t, HashSet<Type> seen)
    {
        if (t.IsByRef || t.IsPointer || t.IsArray) t = t.GetElementType();
        if (t == null || t.IsGenericParameter || !seen.Add(t)) return false;
        if (IsGameAssembly(t.Assembly)) return true;
        return t.IsGenericType && t.GetGenericArguments().Any(a => MentionsGameType(a, seen));
    }

    static bool HasAttribute(MemberInfo m, string name) =>
        m.GetCustomAttributesData().Any(d => d.AttributeType.Name == name || d.AttributeType.Name == name + "Attribute");

    /// <summary>
    /// [Body] 두 규칙을 IL로 확인한다.
    ///   1. 훅 진입점(Prefix/Postfix/Finalizer)은 게임 타입·멤버를 직접 참조하지 않는다 — 참조가 깨지면 훅 본문 JIT가 실패하고,
    ///      Il2CppInterop 트램펄린이 예외를 삼켜 원본 대신 기본값을 돌려주므로 일반 곡까지 그 게임 메서드가 망가진다 (HookGuard 설명).
    ///   2. catch가 있는 메서드가 게임 멤버를 직접 참조하면 그 catch는 바인딩 실패를 못 잡는다 (JIT 단계 예외, .NET 6 실측).
    ///      [Guarded] 메서드는 호출부가 감싸고 시작 시 사전 JIT로 점검하므로 제외한다.
    /// </summary>
    static int VerifyMethodBodies(Assembly modAsm)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        int hooks = 0, tries = 0, methods = 0, failed = 0;
        foreach (var type in InteropNames.SafeGetTypes(modAsm))
        {
            bool patchClass = HasAttribute(type, "HarmonyPatch");
            foreach (var m in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                if (m.IsAbstract || m.GetMethodBody() == null) continue;
                methods++;
                // 3. 게임 record(SongInfo·PatternInfo 등)의 ==/!= 는 인터롭에서 il2cpp_runtime_invoke다. `x == null`이 모든 곡 판정마다 IL2CPP 왕복이 되고,
                //    연산자가 업데이트로 사라지면 그 메서드가 JIT에서 실패한다 → `is null` / `is not null` (참조 비교, 타입 로드·호출 없음).
                var ops = GameReferences(m).Where(r => r.EndsWith(".op_Equality") || r.EndsWith(".op_Inequality")).ToList();
                if (ops.Count > 0)
                {
                    failed++;
                    Console.WriteLine($"   [FAIL Body] {type.FullName}.{m.Name}: 게임 타입의 ==/!= 연산자 호출 (IL2CPP 호출, `is null` 사용): {string.Join(", ", ops)}");
                }

                bool entry = (patchClass || type.Name == "AddressableKeyRedirect") && m.Name is "Prefix" or "Postfix" or "Finalizer";
                bool guarded = HasAttribute(m, "Guarded");
                bool hasCatch = m.GetMethodBody().ExceptionHandlingClauses.Any(c => c.Flags != ExceptionHandlingClauseOptions.Finally);
                if (!entry && !(hasCatch && !guarded)) continue;
                // 자기 시그니처(인자·반환)에 이미 있는 게임 타입을 box/stobj 등 타입 토큰으로 쓰는 것은 새 의존성이 아니다
                // (그 타입이 없으면 패치·호출 자체가 먼저 실패한다). 멤버(메서드·필드) 참조는 여전히 검사한다.
                var own = new HashSet<string>(SignatureTypes(m).Select(t => t.FullName));
                var refs = GameReferences(m).Where(r => !own.Contains(r) && !r.EndsWith(".op_Equality") && !r.EndsWith(".op_Inequality")).ToList();
                if (entry) hooks++; else tries++;
                if (refs.Count == 0) continue;
                failed++;
                var rule = entry ? "훅 본문이 게임 멤버를 직접 참조" : "catch가 있는 비[Guarded] 메서드가 게임 멤버를 직접 참조 (그 catch는 바인딩 실패를 못 잡음)";
                Console.WriteLine($"   [FAIL Body] {type.FullName}.{m.Name}: {rule}: {string.Join(", ", refs.Take(6))}");
            }
        }
        Console.WriteLine($"[Body] 메서드 {methods}개(==/!= 연산자), 훅 본문 {hooks}개, catch 있는 비[Guarded] 메서드 {tries}개 검사, 실패 {failed}개");
        return failed;
    }

    static IEnumerable<Type> SignatureTypes(MethodBase m)
    {
        var types = m.GetParameters().Select(p => p.ParameterType).ToList();
        if (m is MethodInfo mi) types.Add(mi.ReturnType);
        foreach (var t in types)
        {
            var e = t;
            while (e != null && (e.IsByRef || e.IsPointer || e.IsArray)) e = e.GetElementType();
            if (e != null) yield return e;
        }
    }

    /// <summary>
    /// 모드 타입의 필드가 게임 타입에 묶여 있는지 검사한다 ([Layout]).
    /// 실측(순수 .NET 6.0.36, 타입을 지운 라이브러리로 바꿔치기):
    ///   - 게임 값 타입(enum·struct, Nullable·모드 struct 안에 든 것 포함)을 필드로 가진 클래스는 처음 접근할 때 TypeLoadException → 클래스 전체를 못 쓴다.
    ///   - 정적 필드 초기화에서 게임 타입을 제네릭 인자·배열 요소로 쓰면(new List&lt;게임타입&gt;() 등) TypeInitializationException → 영구히 못 쓴다.
    ///   - 게임 참조 타입 필드(SongInfo 등)와, 그런 필드를 가진 모드 클래스를 제네릭 인자로 쓰는 것은 문제없다.
    /// 그래서 이런 필드를 가진 클래스를 일반 곡 훅(판정·입력·OnUpdate)이 건드리면 업데이트 한 번에 모든 곡의 그 게임 메서드가 망가진다.
    /// 게임 값은 int로 저장하고 쓰는 곳(격리 메서드)에서 변환한다.
    /// </summary>
    static int VerifyFieldLayout(Assembly modAsm)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        int fields = 0, failed = 0;
        foreach (var type in InteropNames.SafeGetTypes(modAsm))
        {
            // 컴파일러가 만든 클로저·상태 머신은 그 메서드가 실행될 때만 로드된다 (메서드 자체는 [Guarded]/JIT 검사 대상)
            if (type.Name.Contains('<') || type.GetCustomAttribute<CompilerGeneratedAttribute>() != null) continue;
            foreach (var f in type.GetFields(flags))
            {
                if (f.Name.Contains('<') && !f.Name.Contains("k__BackingField")) continue;
                fields++;
                var reason = GameTypeDependency(f.FieldType, viaGeneric: false, new HashSet<Type>());
                if (reason == null) continue;
                failed++;
                Console.WriteLine($"   [FAIL Layout] {type.FullName}.{f.Name} : {reason}");
            }
        }
        Console.WriteLine($"[Layout] 게임 타입에 묶인 모드 필드 검사: {fields}개 중 실패 {failed}개");
        return failed;
    }

    /// <summary>게임(인터롭) 어셈블리 중 엔진·BCL이 아닌 것. Unity 엔진·Il2Cpp BCL은 Unity 버전이 같으면 바뀌지 않는다.</summary>
    static bool IsGameAssembly(Assembly a)
    {
        string loc;
        try { loc = a.Location; } catch { return false; }
        if (string.IsNullOrEmpty(loc) || !string.Equals(Path.GetDirectoryName(loc), Interop, StringComparison.OrdinalIgnoreCase)) return false;
        var n = a.GetName().Name ?? "";
        return !(n.StartsWith("UnityEngine") || n.StartsWith("Unity.") || n == "Il2Cppmscorlib" || n.StartsWith("Il2CppSystem") || n.StartsWith("Il2CppMono"));
    }

    /// <summary>
    /// 필드 타입이 레이아웃·초기화 단계에서 게임 타입을 로드해야 하면 그 이유를, 아니면 null.
    /// viaGeneric: 제네릭 인자·배열 요소 안 (참조 타입이어도 인스턴스를 만들 때 로드된다).
    /// </summary>
    static string GameTypeDependency(Type t, bool viaGeneric, HashSet<Type> seen)
    {
        if (t.IsByRef || t.IsPointer) t = t.GetElementType();
        if (t == null || t.IsGenericParameter || !seen.Add(t)) return null;
        if (t.IsArray) return GameTypeDependency(t.GetElementType(), true, seen);
        if (IsGameAssembly(t.Assembly))
        {
            if (viaGeneric) return $"게임 타입 {t.FullName}을 제네릭 인자/배열 요소로 사용 (초기화 시 TypeInitializationException)";
            if (t.IsValueType) return $"게임 값 타입 {t.FullName} (클래스 로드 시 TypeLoadException)";
            return null;
        }
        if (t.IsGenericType)
            foreach (var arg in t.GetGenericArguments())
            {
                var r = GameTypeDependency(arg, true, seen);
                if (r != null) return r;
            }
        // 모드·BCL 값 타입(Nullable, 값 튜플, 모드 struct)은 필드까지 펼쳐 본다. 참조 타입의 필드는 그 객체를 만들 때만 문제가 된다.
        if (t.IsValueType && !t.IsPrimitive && !t.IsEnum)
            foreach (var inner in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var r = GameTypeDependency(inner.FieldType, viaGeneric, seen);
                if (r != null) return r;
            }
        return null;
    }

    static readonly HashSet<string> PatchedKeys = new();

    static bool VerifyHarmonyPatch(Type patchClass, CustomAttributeData data, out string error)
    {
        Type targetType = null;
        string methodName = null;
        int? methodType = null;
        Type[] argTypes = null;

        foreach (var arg in data.ConstructorArguments)
        {
            if (arg.Value is Type t) targetType = t;
            else if (arg.Value is string s) methodName = s;
            else if (arg.ArgumentType.IsEnum && arg.Value is int mt) methodType = mt;
            else if (arg.Value is IReadOnlyCollection<CustomAttributeTypedArgument> arr)
                argTypes = arr.Select(x => x.Value as Type).ToArray();
        }
        foreach (var named in data.NamedArguments)
        {
            if (named.MemberName == "declaringType" && named.TypedValue.Value is Type dt) targetType = dt;
            else if (named.MemberName == "methodName" && named.TypedValue.Value is string mn) methodName = mn;
            else if (named.MemberName == "methodType" && named.TypedValue.Value is int mt) methodType = mt;
            else if (named.MemberName == "argumentTypes" && named.TypedValue.Value is IReadOnlyCollection<CustomAttributeTypedArgument> arr)
                argTypes = arr.Select(x => x.Value as Type).ToArray();
        }

        if (targetType == null || string.IsNullOrEmpty(methodName))
        {
            error = "대상 타입 또는 메서드 이름이 비어 있음";
            return false;
        }

        // ModHealth의 필수 패치 이름("선언타입.메서드")과 같은 형식으로 기록 (getter/setter는 get_/set_ 접두사)
        PatchedKeys.Add($"{targetType.Name}.{(methodType == 1 ? "get_" : methodType == 2 ? "set_" : "")}{methodName}");

        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        if (methodType == 1) // Getter
        {
            var prop = targetType.GetProperty(methodName, any);
            if (prop?.GetGetMethod(true) != null || targetType.GetMethod("get_" + methodName, any) != null)
            { error = null; return true; }
            error = $"{targetType.FullName}.{methodName} (Getter) 없음";
            return false;
        }
        if (methodType == 2) // Setter
        {
            var prop = targetType.GetProperty(methodName, any);
            if (prop?.GetSetMethod(true) != null || targetType.GetMethod("set_" + methodName, any) != null)
            { error = null; return true; }
            error = $"{targetType.FullName}.{methodName} (Setter) 없음";
            return false;
        }
        if (argTypes != null)
        {
            var m = targetType.GetMethod(methodName, any, null, argTypes, null);
            if (m != null) { error = null; return true; }
            error = $"{targetType.FullName}.{methodName}({string.Join(", ", argTypes.Select(t => t?.Name ?? "?"))}) 없음";
            return false;
        }
        var matches = targetType.GetMethods(any).Where(m => m.Name == methodName).ToArray();
        if (matches.Length == 1) { error = null; return true; }
        error = matches.Length == 0
            ? $"{targetType.FullName}.{methodName} 없음"
            : $"{targetType.FullName}.{methodName} 오버로드 {matches.Length}개 (AmbiguousMatch 위험)";
        return false;
    }
}
