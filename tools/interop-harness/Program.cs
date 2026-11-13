// 오프라인 인터롭 하네스 — 게임을 실행하지 않고 MelonLoader가 만든 인터롭 어셈블리로 검증한다.
//   dotnet run -c Release            : InteropNames 신호별 누락·오탐, 람다 메서드 판별, Locate 엄격 조건 후보 수
//   dotnet run -c Release -- explore : 컴파일러 생성 타입의 멤버(프로퍼티) 이름 빈도 (멤버 모양 신호 조사용)
// 정답 기준: 인터롭 [ObfuscatedName]의 원래 이름(마지막 중첩 이름)에 '<'가 있거나, 이름에 '<'가 그대로 남은 타입.
// 게임 경로는 환경 변수 PLATINALAB_GAME으로 바꿀 수 있다. 게임 업데이트 후 MelonLoader가 인터롭을 다시 만들면 실행한다.
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
    static void Main(string[] args)
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
        if (args.Length > 0 && args[0] == "explore") { Explore(asms); return; }
        Evaluate(asms);
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
}
