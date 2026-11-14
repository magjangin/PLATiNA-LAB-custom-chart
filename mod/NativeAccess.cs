using Il2CppInterop.Runtime;

namespace PLATiNALAB.Mod;

/// <summary>
/// IL2CPP 문자열(UTF-16 버퍼)을 관리 문자열로 바꾸지 않고 검사한다.
/// 모든 곡·모든 리소스 로드에서 도는 훅(Locate, SongInfo 판정)이 커스텀 곡이 아닌 경우를 할당 없이 걸러 내는 데 쓴다.
/// </summary>
internal static class NativeStrings
{
    public static unsafe bool Contains(IntPtr il2cppString, string needle)
    {
        if (!TryGetChars(il2cppString, out var chars, out int length) || length < needle.Length) return false;
        return new ReadOnlySpan<char>(chars, length).IndexOf(needle.AsSpan(), StringComparison.Ordinal) >= 0;
    }

    public static unsafe bool StartsWith(IntPtr il2cppString, string prefix)
    {
        if (!TryGetChars(il2cppString, out var chars, out int length) || length < prefix.Length) return false;
        return new ReadOnlySpan<char>(chars, prefix.Length).SequenceEqual(prefix.AsSpan());
    }

    /// <summary>IL2CPP 문자열과 관리 문자열이 같은지 (null은 IntPtr.Zero와 같다고 본다). 자체 검증용.</summary>
    public static unsafe bool EqualsManaged(IntPtr il2cppString, string managed)
    {
        if (il2cppString == IntPtr.Zero || managed == null) return il2cppString == IntPtr.Zero && managed == null;
        if (!TryGetChars(il2cppString, out var chars, out int length) || length != managed.Length) return false;
        return new ReadOnlySpan<char>(chars, length).SequenceEqual(managed.AsSpan());
    }

    private static unsafe bool TryGetChars(IntPtr il2cppString, out char* chars, out int length)
    {
        chars = null;
        length = 0;
        if (il2cppString == IntPtr.Zero) return false;
        length = IL2CPP.il2cpp_string_length(il2cppString);
        chars = IL2CPP.il2cpp_string_chars(il2cppString);
        return chars != null && length >= 0;
    }
}

/// <summary>
/// IL2CPP 객체의 필드를 오프셋으로 직접 읽는다 (자동 프로퍼티 백킹 필드 등).
///
/// 인터롭 프로퍼티 게터(SongInfo.Id, PatternInfo.PatternIndex 등)는 il2cpp_runtime_invoke로 게임 메서드를 부르고
/// 문자열이면 관리 문자열까지 만든다. 선곡 화면은 곡마다 IsLock/IsAvailable 등을 반복해서 부르므로 이 비용이 곡 수만큼 쌓인다.
/// 필드 위치는 인터롭 이름이 아니라 IL2CPP 원래 이름(&lt;Id&gt;k__BackingField)으로 런타임에 찾으므로 인터롭 재생성과 무관하고,
/// 찾지 못하거나 오프셋이 이상하면 꺼진다.
/// 호출부는 처음 <see cref="SelfCheckCount"/>건을 관리 경로 결과와 비교해(<see cref="NeedsSelfCheck"/>, <see cref="ReportCheck"/>)
/// 하나라도 다르면 이 리더를 끄고 관리 경로만 쓴다 (AddressableKeyRedirect의 네이티브 문자열 검사와 같은 방식).
/// </summary>
internal sealed class NativeField
{
    public const int SelfCheckCount = 32;

    private sealed class Resolved
    {
        public readonly IntPtr Class;
        public readonly int Offset;
        public Resolved(IntPtr klass, int offset) { Class = klass; Offset = offset; }
    }

    private readonly string _label;
    private readonly string _fieldName;
    private volatile Resolved _resolved; // 여러 스레드가 읽으므로 클래스와 오프셋을 한 객체로 바꿔 끼운다
    private volatile bool _broken;
    private int _checks;

    public NativeField(string label, string il2cppFieldName)
    {
        _label = label;
        _fieldName = il2cppFieldName;
    }

    public bool NeedsSelfCheck => !_broken && Volatile.Read(ref _checks) < SelfCheckCount;

    public void ReportCheck(bool match, string detail)
    {
        if (_broken) return;
        if (!match)
        {
            Disable($"관리 경로와 결과가 다름 ({detail})");
            return;
        }
        if (Interlocked.Increment(ref _checks) == SelfCheckCount)
            Core.Log.Msg($"[Native] {_label} 직접 읽기 자체 검증 {SelfCheckCount}건 일치");
    }

    public void Disable(string reason)
    {
        if (_broken) return;
        _broken = true;
        Core.Log.Warning($"[Native] {_label} 직접 읽기를 끄고 관리 경로로 전환: {reason}");
    }

    public unsafe bool TryReadPointer(IntPtr obj, out IntPtr value)
    {
        if (TryAddress(obj, out var address)) { value = *(IntPtr*)address; return true; }
        value = IntPtr.Zero;
        return false;
    }

    public unsafe bool TryReadInt32(IntPtr obj, out int value)
    {
        if (TryAddress(obj, out var address)) { value = *(int*)address; return true; }
        value = 0;
        return false;
    }

    private bool TryAddress(IntPtr obj, out IntPtr address)
    {
        address = IntPtr.Zero;
        if (_broken || obj == IntPtr.Zero) return false;
        var klass = IL2CPP.il2cpp_object_get_class(obj);
        if (klass == IntPtr.Zero) return false;
        var r = _resolved;
        if (r == null || r.Class != klass)
        {
            r = Resolve(klass);
            if (r == null) return false;
        }
        address = obj + r.Offset;
        return true;
    }

    private Resolved Resolve(IntPtr klass)
    {
        try
        {
            // il2cpp_class_get_field_from_name은 부모 클래스까지 찾는다
            var field = IL2CPP.il2cpp_class_get_field_from_name(klass, _fieldName);
            if (field == IntPtr.Zero) { Disable($"필드 {_fieldName} 없음"); return null; }
            int offset = (int)IL2CPP.il2cpp_field_get_offset(field);
            // 0..헤더(클래스 포인터 + 모니터)는 객체 필드가 아니다. 지나치게 큰 값도 잘못 찾은 것으로 본다.
            if (offset < IntPtr.Size * 2 || offset > 4096) { Disable($"필드 {_fieldName} 오프셋 비정상 ({offset})"); return null; }
            var r = new Resolved(klass, offset);
            _resolved = r;
            return r;
        }
        catch (Exception e)
        {
            Disable($"필드 {_fieldName} 조회 실패: {e.Message}");
            return null;
        }
    }
}
