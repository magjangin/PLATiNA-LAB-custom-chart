using System.Collections.Concurrent;

namespace PLATiNALAB.Mod;

/// <summary>
/// 공식 곡의 소유·해금 판정(IsAvailable / IsNotOwned / IsLock)을 곡 ID별로 기억하는 순수 저장소.
/// 게임 타입을 쓰지 않아 하네스가 그대로 검증한다. 파일 입출력·훅은 <see cref="SongOwnership"/>이 맡는다.
///
/// 왜 필요한가: 오프라인(OfflineMode=1)에서는 서버가 내려주던 보유·해금 정보가 없어 원본 판정이 KeyNotFound를 던진다.
/// 예전(decf3b7)에는 모든 공식 곡을 무조건 해금해 소유하지 않은 유료 DLC 곡까지 열렸다.
/// 지금은 온라인(OfflineMode=0)으로 실행할 때 게임이 실제로 돌려준 값을 기록하고, 오프라인에서는 그 기록만 재현한다.
/// 기록에 없는 곡은 잠긴 쪽(사용 불가·미보유·잠김)으로 답한다.
///
/// 저장 형식: { "곡ID": 비트 } — 판정 k(0~2)마다 2비트(bit 2k = 기록 있음, bit 2k+1 = 값).
/// </summary>
internal sealed class SongOwnershipStore
{
    public const int Available = 0, NotOwned = 1, Lock = 2;
    private const int KindCount = 3;

    private readonly ConcurrentDictionary<string, int> _bits;
    private readonly object _lock = new();

    public SongOwnershipStore(Dictionary<string, int> initial = null)
    {
        _bits = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        if (initial == null) return;
        const int validMask = (1 << (KindCount * 2)) - 1;
        foreach (var (k, v) in initial)
        {
            int bits = v & validMask; // 범위 밖 비트만 있는 항목은 기록이 아니다
            if (!string.IsNullOrEmpty(k) && bits > 0) _bits[k] = bits;
        }
    }

    public int Count => _bits.Count;

    /// <summary>게임 판정을 기억한다. 값이 바뀌었으면 true (저장이 필요함).</summary>
    public bool Record(string songId, int kind, bool value)
    {
        if (string.IsNullOrEmpty(songId) || (uint)kind >= KindCount) return false;
        int known = 1 << (kind * 2), val = 1 << (kind * 2 + 1);
        lock (_lock)
        {
            _bits.TryGetValue(songId, out int old);
            int next = (old | known) & ~val | (value ? val : 0);
            if (next == old) return false;
            _bits[songId] = next;
            return true;
        }
    }

    /// <summary>오프라인 선곡 핫패스 조회: ConcurrentDictionary로 lock 없이 읽는다 (24차).</summary>
    public bool TryGet(string songId, int kind, out bool value)
    {
        value = false;
        if (string.IsNullOrEmpty(songId) || (uint)kind >= KindCount) return false;
        int known = 1 << (kind * 2), val = 1 << (kind * 2 + 1);
        if (!_bits.TryGetValue(songId, out int bits) || (bits & known) == 0) return false;
        value = (bits & val) != 0;
        return true;
    }

    /// <summary>기록이 없을 때의 답: 사용 불가·미보유·잠김 (소유를 확인할 수 없는 곡은 열지 않는다).</summary>
    public static bool LockedDefault(int kind) => kind != Available;

    /// <summary>기록이 있으면 그 값, 없으면 <see cref="LockedDefault"/>. 두 번째 값은 기록이 있었는지.</summary>
    public bool Resolve(string songId, int kind, out bool known)
    {
        known = TryGet(songId, kind, out bool value);
        return known ? value : LockedDefault(kind);
    }

    public Dictionary<string, int> Snapshot() =>
        new(_bits, StringComparer.Ordinal);
}
