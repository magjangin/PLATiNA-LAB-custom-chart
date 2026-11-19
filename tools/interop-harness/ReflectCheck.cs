// [Reflect] 리플렉션 폴백 이름 검증 (11차 감사).
// UpdateResilience.SafeSetFieldOrProperty / SafeGetFieldOrProperty에 넘기는 문자열 멤버 이름은 직접 경로가 깨졌을 때만 쓰이므로
// 이름 오타나 업데이트로 사라진 멤버가 실기에서 가장 늦게 드러난다. 모드 IL을 스택 시뮬레이션해 호출부마다
// (대상 인자의 정적 타입, 이름 문자열)을 뽑고, 모드 자신의 UpdateResilience.CanResolve로 인터롭 타입에서 찾히는지 확인한다.
// 자기 인자를 그대로 넘기는 메서드(SetSongField, CopyMember 등)는 "이름 전달 래퍼"로 인식해 그 호출부를 대신 검사한다.
using System.Reflection;
using System.Reflection.Emit;

static class ReflectCheck
{
    sealed class Slot
    {
        public string Literal;
        public Type Type;
        public int Arg = -1;
        public Slot Copy() => (Slot)MemberwiseClone();
    }

    sealed record Sink(int Target, int Name, bool Write);

    static readonly Dictionary<short, OpCode> Ops =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => o.Value);

    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static int Run(Assembly modAsm, string interopDir)
    {
        var ur = modAsm.GetType("PLATiNALAB.Mod.UpdateResilience");
        var canResolve = ur?.GetMethod("CanResolve", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (canResolve == null)
        {
            Console.WriteLine("   [FAIL Reflect] UpdateResilience.CanResolve를 찾지 못함");
            return 1;
        }

        // 자기 검증: CanResolve가 항상 true/false가 되는 퇴행을 막는다. SongInfo.Id는 getter만 있어
        // 인터롭 백킹 프로퍼티(_Id_k__BackingField)로만 쓸 수 있다 — 10차까지의 필드 전용 탐색은 여기서 false였다.
        var songInfo = Type.GetType("Il2CppPlatinaLab.Main.Catalog.Scheme.Song.SongInfo, Il2CppPlatinaLab.Main", throwOnError: false);
        if (songInfo == null
            || !(bool)canResolve.Invoke(null, new object[] { songInfo, "Id", true })
            || (bool)canResolve.Invoke(null, new object[] { songInfo, "__no_such_member__", false }))
        {
            Console.WriteLine("   [FAIL Reflect] CanResolve 자기 검증 실패 (SongInfo.Id 쓰기 = true, 없는 이름 = false 기대)");
            return 1;
        }

        var sinks = new Dictionary<MethodBase, List<Sink>>();
        foreach (var m in ur.GetMethods(All))
        {
            if (m.Name == "SafeSetFieldOrProperty") sinks[m] = new() { new Sink(0, 1, true) };
            else if (m.Name == "SafeGetFieldOrProperty") sinks[m] = new() { new Sink(0, 1, false) };
        }

        var methods = PLATiNALAB.Mod.InteropNames.SafeGetTypes(modAsm)
            .SelectMany(t => t.GetMethods(All).Cast<MethodBase>().Concat(t.GetConstructors(All)))
            .Where(m => !m.IsAbstract && m.GetMethodBody() != null && m.DeclaringType != ur)
            .ToList();
        var sites = methods.ToDictionary(m => m, CallSites);

        // 1단계: 이름 전달 래퍼 찾기 (고정점까지 반복)
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var (m, calls) in sites)
            foreach (var (callee, args) in calls)
            {
                if (!sinks.TryGetValue(Normalize(callee), out var list)) continue;
                foreach (var s in list)
                {
                    if (s.Target >= args.Length || s.Name >= args.Length) continue;
                    var name = args[s.Name];
                    var target = args[s.Target];
                    if (name.Literal != null || name.Arg < 0 || target.Arg < 0) continue;
                    var wrapper = sinks.TryGetValue(m, out var existing) ? existing : sinks[m] = new();
                    var sink = new Sink(target.Arg, name.Arg, s.Write);
                    if (!wrapper.Contains(sink)) { wrapper.Add(sink); changed = true; }
                }
            }
        }

        // 2단계: 문자열 이름 호출부 검증
        int checkedCount = 0, failed = 0;
        foreach (var (m, calls) in sites)
        foreach (var (callee, args) in calls)
        {
            if (!sinks.TryGetValue(Normalize(callee), out var list)) continue;
            foreach (var s in list)
            {
                if (s.Target >= args.Length || s.Name >= args.Length) continue;
                var name = args[s.Name];
                var target = args[s.Target];
                if (name.Literal == null && name.Arg >= 0 && target.Arg >= 0 && sinks.ContainsKey(m)) continue; // 래퍼 본문
                var where = $"{m.DeclaringType?.Name}.{m.Name} → {callee.Name}";
                checkedCount++;
                if (name.Literal == null)
                {
                    failed++;
                    Console.WriteLine($"   [FAIL Reflect] {where}: 이름이 문자열 상수가 아님 (오프라인 검증 불가 — 호출부에 상수로 두거나 래퍼로 분리)");
                    continue;
                }
                var type = target.Type;
                if (type == null || type == typeof(object) || !IsInterop(type, interopDir))
                {
                    failed++;
                    Console.WriteLine($"   [FAIL Reflect] {where}(\"{name.Literal}\"): 대상의 정적 타입이 인터롭 타입이 아님 ({type?.FullName ?? "불명"})");
                    continue;
                }
                bool ok;
                try { ok = (bool)canResolve.Invoke(null, new object[] { type, name.Literal, s.Write }); }
                catch (Exception e) { ok = false; Console.WriteLine($"   [FAIL Reflect] CanResolve 예외: {e.InnerException?.Message ?? e.Message}"); }
                if (ok) continue;
                failed++;
                Console.WriteLine($"   [FAIL Reflect] {where}: {type.Name}에서 '{name.Literal}'({(s.Write ? "쓰기" : "읽기")})를 찾지 못함");
            }
        }
        int wrappers = sinks.Count(kv => kv.Key.DeclaringType != ur);
        Console.WriteLine($"[Reflect] 리플렉션 폴백 이름 검증: 호출부 {checkedCount}개(이름 전달 래퍼 {wrappers}개) 중 실패 {failed}개");
        return checkedCount == 0 ? 1 : failed;
    }

    /// <summary>
    /// [GuardKey] HookGuard 키 정합성 (10차 감사의 수작업 전수 대조를 기계화).
    /// HookGuard.IsDisabled(키)는 그 키가 [Guarded] 이름이거나 어딘가에서 HookGuard.Fail(키)로 보고될 때만 true가 될 수 있다.
    /// 둘 다 아니면 오타 등으로 단락이 절대 일어나지 않아, 업데이트 후 매 호출 예외가 반복된다.
    /// 반대로 HookGuard.Fail(키)로 끄는 키를 IsDisabled로 단락하는 곳이 없으면, 꺼진 뒤에도 매 호출 예외가 반복되므로 실패로 본다.
    /// 키가 문자열 상수가 아니면(자기 인자 전달 래퍼 제외) 검증할 수 없으므로 실패로 본다.
    /// </summary>
    public static int RunGuardKeys(Assembly modAsm)
    {
        var hg = modAsm.GetType("PLATiNALAB.Mod.HookGuard");
        if (hg == null) { Console.WriteLine("   [FAIL GuardKey] HookGuard 타입 없음"); return 1; }
        var types = PLATiNALAB.Mod.InteropNames.SafeGetTypes(modAsm);
        var guarded = new HashSet<string>();
        foreach (var t in types)
        foreach (var m in t.GetMethods(All))
        foreach (var d in m.GetCustomAttributesData())
            if (d.AttributeType.Name == "GuardedAttribute" && d.ConstructorArguments.Count > 0 && d.ConstructorArguments[0].Value is string k)
                guarded.Add(k);

        var methods = types.SelectMany(t => t.GetMethods(All).Cast<MethodBase>().Concat(t.GetConstructors(All)))
            .Where(m => !m.IsAbstract && m.GetMethodBody() != null && m.DeclaringType != hg).ToList();
        var checks = new List<(string where, string key)>();
        var failKeys = new HashSet<string>();
        int failed = 0;
        foreach (var m in methods)
        foreach (var (callee, args) in CallSites(m))
        {
            if (callee.DeclaringType != hg || (callee.Name != "IsDisabled" && callee.Name != "Fail") || args.Length == 0) continue;
            var key = args[0];
            var where = $"{m.DeclaringType?.Name}.{m.Name} → HookGuard.{callee.Name}";
            if (key.Literal == null)
            {
                if (key.Arg >= 0) continue; // 키를 인자로 받는 공통 헬퍼: 그 호출부의 상수는 [Guarded]/Fail 쪽에서 드러난다
                failed++;
                Console.WriteLine($"   [FAIL GuardKey] {where}: 키가 문자열 상수가 아님");
                continue;
            }
            if (callee.Name == "Fail") failKeys.Add(key.Literal);
            else checks.Add((where, key.Literal));
        }
        foreach (var (where, key) in checks)
        {
            if (guarded.Contains(key) || failKeys.Contains(key)) continue;
            failed++;
            Console.WriteLine($"   [FAIL GuardKey] {where}(\"{key}\"): [Guarded]·HookGuard.Fail 어디에도 없는 키 — 이 단락은 절대 켜지지 않음");
        }
        // [Guarded]인데 Fail로도 IsDisabled로도 쓰이지 않는 키는 사전 점검에서만 보고되는 "보고 전용" 이름이다 (정상, 개수만 표시)
        var checkedKeys = new HashSet<string>(checks.Select(c => c.key));
        // Fail로 끄기만 하고 IsDisabled로 단락하지 않는 키: 꺼진 뒤에도 매 호출 같은 바인딩 예외가 반복된다 (9·10차 U9-3·U10-2·U10-3 유형)
        foreach (var k in failKeys.Where(k => !checkedKeys.Contains(k)).OrderBy(k => k))
        {
            failed++;
            Console.WriteLine($"   [FAIL GuardKey] '{k}': HookGuard.Fail로 끄지만 IsDisabled로 단락하는 곳이 없음 — 꺼진 뒤에도 매 호출 예외 반복");
        }
        int reportOnly = guarded.Count(k => !checkedKeys.Contains(k));
        Console.WriteLine($"[GuardKey] HookGuard 키 정합성: IsDisabled 호출 {checks.Count}개·Fail 키 {failKeys.Count}종·[Guarded] 키 {guarded.Count}종(보고 전용 {reportOnly}종) 중 실패 {failed}개");
        return checks.Count == 0 ? 1 : failed;
    }

    static MethodBase Normalize(MethodBase m) => m is MethodInfo mi && mi.IsGenericMethod ? mi.GetGenericMethodDefinition() : m;

    static bool IsInterop(Type t, string interopDir)
    {
        try { return string.Equals(Path.GetDirectoryName(t.Assembly.Location), interopDir, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>
    /// 선형 IL 스캔 + 분기 대상 스택 스냅숏으로 call/callvirt 호출부의 인자 슬롯(문자열 상수·정적 타입·자기 인자 번호)을 뽑는다.
    /// C# 컴파일러 출력(전방 분기, 삼항·?? 연산자)에 충분한 근사이며 완전한 데이터 흐름 분석은 아니다.
    /// </summary>
    static List<(MethodBase callee, Slot[] args)> CallSites(MethodBase m)
    {
        var result = new List<(MethodBase, Slot[])>();
        var body = m.GetMethodBody();
        byte[] il;
        try { il = body?.GetILAsByteArray(); } catch { return result; }
        if (il == null) return result;
        var locals = body.LocalVariables;
        var ps = m.GetParameters();
        bool inst = !m.IsStatic;
        Type[] typeArgs = m.DeclaringType?.IsGenericType == true ? m.DeclaringType.GetGenericArguments() : null;
        Type[] methodArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;

        Type ArgType(int i)
        {
            if (inst) { if (i == 0) return m.DeclaringType; i--; }
            return i >= 0 && i < ps.Length ? ps[i].ParameterType : null;
        }

        var snapshots = new Dictionary<int, List<Slot>>();
        foreach (var c in body.ExceptionHandlingClauses)
        {
            snapshots[c.HandlerOffset] = c.Flags == ExceptionHandlingClauseOptions.Clause ? new List<Slot> { new() { Type = c.CatchType } } : new List<Slot>();
            if (c.Flags == ExceptionHandlingClauseOptions.Filter) snapshots[c.FilterOffset] = new List<Slot> { new() };
        }
        var stack = new List<Slot>();
        Slot Pop() { if (stack.Count == 0) return new Slot(); var s = stack[^1]; stack.RemoveAt(stack.Count - 1); return s; }
        void Branch(int target) { if (!snapshots.ContainsKey(target)) snapshots[target] = stack.Select(s => s.Copy()).ToList(); }

        bool fellThrough = true;
        int i = 0;
        while (i < il.Length)
        {
            if (snapshots.TryGetValue(i, out var snap) && (!fellThrough || stack.Count == 0)) stack = snap.Select(s => s.Copy()).ToList();
            else if (!fellThrough) stack.Clear();
            fellThrough = true;

            short value = il[i] == 0xFE && i + 1 < il.Length ? (short)(0xFE00 | il[i + 1]) : il[i];
            if (!Ops.TryGetValue(value, out var op)) break;
            int p = i + op.Size;
            int operandSize = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, p) * 4,
                _ => 4,
            };
            int next = p + operandSize;
            int Int() => BitConverter.ToInt32(il, p);
            int VarIndex() => op.OperandType == OperandType.ShortInlineVar ? il[p] : op.OperandType == OperandType.InlineVar ? BitConverter.ToUInt16(il, p) : -1;

            MemberInfo Resolve()
            {
                try { return m.Module.ResolveMember(Int(), typeArgs, methodArgs); } catch { return null; }
            }

            string n = op.Name;
            if (op == OpCodes.Ldstr)
            {
                string lit = null;
                try { lit = m.Module.ResolveString(Int()); } catch { }
                stack.Add(new Slot { Literal = lit, Type = typeof(string) });
            }
            else if (n.StartsWith("ldarg") && !n.StartsWith("ldarga"))
            {
                int idx = op.OperandType == OperandType.InlineNone ? n[^1] - '0' : VarIndex();
                stack.Add(new Slot { Arg = idx, Type = ArgType(idx) });
            }
            else if (n.StartsWith("ldloc") && !n.StartsWith("ldloca"))
            {
                int idx = op.OperandType == OperandType.InlineNone ? n[^1] - '0' : VarIndex();
                stack.Add(new Slot { Type = idx >= 0 && idx < locals.Count ? locals[idx].LocalType : null });
            }
            else if (op == OpCodes.Ldfld || op == OpCodes.Ldsfld)
            {
                if (op == OpCodes.Ldfld) Pop();
                stack.Add(new Slot { Type = (Resolve() as FieldInfo)?.FieldType });
            }
            else if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
            {
                var callee = Resolve() as MethodBase;
                if (callee == null) { stack.Clear(); i = next; continue; }
                bool hasThis = !callee.IsStatic && op != OpCodes.Newobj;
                int count = callee.GetParameters().Length + (hasThis ? 1 : 0);
                var args = new Slot[count];
                for (int k = count - 1; k >= 0; k--) args[k] = Pop();
                if (op != OpCodes.Newobj) result.Add((callee, args));
                if (op == OpCodes.Newobj) stack.Add(new Slot { Type = callee.DeclaringType });
                else if (callee is MethodInfo mi && mi.ReturnType != typeof(void)) stack.Add(new Slot { Type = mi.ReturnType });
            }
            else if (op == OpCodes.Dup)
            {
                var top = stack.Count > 0 ? stack[^1].Copy() : new Slot();
                stack.Add(top);
            }
            else if (op == OpCodes.Castclass || op == OpCodes.Isinst || op == OpCodes.Box || op == OpCodes.Unbox_Any)
            {
                Pop();
                stack.Add(new Slot { Type = Resolve() as Type });
            }
            else if (op == OpCodes.Ret || op == OpCodes.Throw || op == OpCodes.Rethrow || op == OpCodes.Endfinally || op == OpCodes.Leave || op == OpCodes.Leave_S)
            {
                if (op == OpCodes.Leave || op == OpCodes.Leave_S) Branch(next + (op == OpCodes.Leave_S ? (sbyte)il[p] : Int()));
                stack.Clear();
                fellThrough = false;
            }
            else
            {
                int pops = op.StackBehaviourPop switch
                {
                    StackBehaviour.Pop0 => 0,
                    StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
                    StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8
                        or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref
                        or StackBehaviour.Popref_popi_pop1 => 3,
                    StackBehaviour.Varpop => 0,
                    _ => 2,
                };
                for (int k = 0; k < pops; k++) Pop();
                if (op.OperandType == OperandType.ShortInlineBrTarget) Branch(next + (sbyte)il[p]);
                else if (op.OperandType == OperandType.InlineBrTarget) Branch(next + Int());
                else if (op.OperandType == OperandType.InlineSwitch)
                {
                    int cases = Int();
                    for (int k = 0; k < cases; k++) Branch(next + BitConverter.ToInt32(il, p + 4 + k * 4));
                }
                int pushes = op.StackBehaviourPush switch
                {
                    StackBehaviour.Push0 => 0,
                    StackBehaviour.Push1_push1 => 2,
                    StackBehaviour.Varpush => 0,
                    _ => 1,
                };
                for (int k = 0; k < pushes; k++) stack.Add(new Slot());
                if (op.FlowControl == FlowControl.Branch) { stack.Clear(); fellThrough = false; }
            }
            i = next;
        }
        return result;
    }
}
