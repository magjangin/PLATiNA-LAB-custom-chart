using System.Text;

namespace PLATiNALAB.Mod;

/// <summary>
/// 한국어, 중국어(간체 GBK/GB2312, 번체 Big5), 일본어(Shift-JIS), UTF-8 인코딩을 자동 감지하여 텍스트를 읽는 헬퍼.
/// BMS 및 메타 파일이 다양한 아시아권 로케일 환경에서 생성되었을 때 글자 깨짐 및 파싱 실패를 원천 방지한다.
/// </summary>
internal static class EncodingHelper
{
    static EncodingHelper()
    {
        // .NET Core/6.0 이상에서 CP949, CP936(GBK), CP932(Shift-JIS) 등 코드페이지를 지원하기 위해 등록
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[EncodingHelper] CodePagesEncodingProvider 등록 실패: {e.Message}");
        }
    }

    /// <summary>
    /// 파일 바이트를 분석하여 적절한 인코딩으로 모든 줄을 읽어온다.
    /// 1. UTF-8 BOM 또는 유효한 UTF-8 바이트 시퀀스 검사
    /// 2. 실패 시 Shift-JIS(CP932) / 한국어 CP949 / 중국어 GBK(CP936) / Big5(CP950) 중 점수가 가장 높은 것 (DetectEncoding)
    /// </summary>
    public static string[] ReadAllLinesSafe(string path)
    {
        if (!File.Exists(path)) return Array.Empty<string>();
        try
        {
            var bytes = File.ReadAllBytes(path);
            var encoding = DetectEncoding(bytes);
            var text = encoding.GetString(bytes);
            return text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        }
        catch (Exception e)
        {
            Core.Log.Warning($"[EncodingHelper] {path} 안전 읽기 실패, 기본 UTF-8 시도: {e.Message}");
            return File.ReadAllLines(path);
        }
    }

    /// <summary>
    /// 바이트 시퀀스 기반 인코딩 자동 감지.
    /// UTF-8이 아니면 Shift-JIS(932) / CP949(949) / GBK(936) / Big5(950)로 각각 읽었을 때 "그 언어다운" 정도를 점수로 매긴다.
    /// 2바이트 영역이 서로 겹쳐서(예: EUC-KR 한글 B0A1~C8FE = GB2312 한자 영역) 디코딩 성공 여부만으로는 구분이 안 된다.
    ///   932: 히라가나/가타카나 +3, 한자 +1, 반각 가타카나(한국어·중국어 바이트를 932로 읽으면 대량 발생) -1
    ///   949: KS X 1001 한글(B0~C8) +3, 한자(CA~FD, 한국어 본문엔 드묾) -2, 확장 한글(UHC, 일본어를 949로 읽으면 발생) -1
    ///   936: GB2312 상용 한자(B0~F7) +2, 그 밖의 GBK 확장 -1
    ///   950: Big5 상용 한자(A4~C6) +2, 차상용(C9~F9) +1
    /// 해당 인코딩으로 불가능한 바이트가 나오면 후보에서 뺀다. 동점이면 949 → 932 → 936 → 950 순.
    /// </summary>
    public static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return Encoding.UTF8;

        // 1. BOM 검사
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode; // UTF-16 LE
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        // 2. UTF-8 유효성 검사 (엄격 모드)
        if (IsValidUtf8(bytes))
            return Encoding.UTF8;

        // 3. 아시아권 2바이트 인코딩 점수 비교
        int codePage = DetectCodePage(bytes);
        try { return Encoding.GetEncoding(codePage); }
        catch
        {
            try { return Encoding.GetEncoding(949); }
            catch { return Encoding.UTF8; }
        }
    }

    /// <summary>점수가 가장 높은 코드 페이지 (테스트용으로 분리).</summary>
    internal static int DetectCodePage(byte[] bytes)
    {
        int best = 949;
        long bestScore = long.MinValue;
        foreach (var (cp, scorer) in s_scorers)
        {
            long score = scorer(bytes);
            if (score > bestScore) { bestScore = score; best = cp; }
        }
        return best;
    }

    private const long Invalid = long.MinValue / 2;

    private static readonly (int cp, Func<byte[], long> scorer)[] s_scorers =
    {
        (949, ScoreCp949),
        (932, ScoreShiftJis),
        (936, ScoreGbk),
        (950, ScoreBig5),
    };

    private static long ScoreShiftJis(byte[] b)
    {
        long score = 0;
        for (int i = 0; i < b.Length; i++)
        {
            int c = b[i];
            if (c < 0x80) continue;
            if (c >= 0xA1 && c <= 0xDF) { score -= 1; continue; }              // 반각 가타카나
            bool lead = (c >= 0x81 && c <= 0x9F) || (c >= 0xE0 && c <= 0xFC);
            if (!lead || i + 1 >= b.Length) return Invalid;
            int t = b[++i];
            if (t < 0x40 || t == 0x7F || t > 0xFC) return Invalid;
            if ((c == 0x82 && t >= 0x9F && t <= 0xF1) || (c == 0x83 && t >= 0x40 && t <= 0x96)) score += 3; // 히라가나/가타카나
            else if ((c >= 0x88 && c <= 0x9F) || (c >= 0xE0 && c <= 0xEA)) score += 1;                   // 한자
        }
        return score;
    }

    private static long ScoreCp949(byte[] b)
    {
        long score = 0;
        for (int i = 0; i < b.Length; i++)
        {
            int c = b[i];
            if (c < 0x80) continue;
            if (c < 0x81 || c > 0xFE || i + 1 >= b.Length) return Invalid;
            int t = b[++i];
            bool eucTrail = t >= 0xA1 && t <= 0xFE;
            if (c >= 0xB0 && c <= 0xC8 && eucTrail) score += 3;                  // KS X 1001 한글
            else if (c >= 0xCA && c <= 0xFD && eucTrail) score -= 2;             // 한자
            else if (c >= 0xA1 && c <= 0xAF && eucTrail) { }                     // 기호
            else if (c <= 0xC6 && ((t >= 0x41 && t <= 0x5A) || (t >= 0x61 && t <= 0x7A) || (t >= 0x81 && t <= 0xFE))) score -= 1; // UHC 확장 한글
            else if (eucTrail) score -= 1;                                       // 사용자 정의 영역 등
            else return Invalid;
        }
        return score;
    }

    private static long ScoreGbk(byte[] b)
    {
        long score = 0;
        for (int i = 0; i < b.Length; i++)
        {
            int c = b[i];
            if (c < 0x80) continue;
            if (c < 0x81 || c > 0xFE || i + 1 >= b.Length) return Invalid;
            int t = b[++i];
            if (t < 0x40 || t == 0x7F || t > 0xFE) return Invalid;
            bool eucTrail = t >= 0xA1;
            if (c >= 0xB0 && c <= 0xF7 && eucTrail) score += 2;                  // GB2312 한자
            else if (c >= 0xA1 && c <= 0xA9 && eucTrail) { }                     // 기호
            else score -= 1;                                                     // GBK 확장
        }
        return score;
    }

    private static long ScoreBig5(byte[] b)
    {
        long score = 0;
        for (int i = 0; i < b.Length; i++)
        {
            int c = b[i];
            if (c < 0x80) continue;
            if (c < 0xA1 || c > 0xF9 || i + 1 >= b.Length) return Invalid;
            int t = b[++i];
            if (!((t >= 0x40 && t <= 0x7E) || (t >= 0xA1 && t <= 0xFE))) return Invalid;
            if (c >= 0xA4 && c <= 0xC6) score += 2;                              // 상용 한자
            else if (c >= 0xC9) score += 1;                                      // 차상용 한자
        }
        return score;
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        int i = 0;
        int len = bytes.Length;
        while (i < len)
        {
            byte b = bytes[i++];
            if (b <= 0x7F) continue; // ASCII
            int count;
            if ((b & 0xE0) == 0xC0) count = 1;
            else if ((b & 0xF0) == 0xE0) count = 2;
            else if ((b & 0xF8) == 0xF0) count = 3;
            else return false;

            if (i + count > len) return false;
            for (int j = 0; j < count; j++)
            {
                byte next = bytes[i++];
                if ((next & 0xC0) != 0x80) return false;
            }
        }
        return true;
    }
}
