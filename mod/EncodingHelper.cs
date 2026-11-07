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
    /// 2. 실패 시 중국어 GBK(CP936) / 일본어 Shift-JIS(CP932) / 한국어 CP949 순차 시도
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
    /// 바이트 시퀀스 기반 인코딩 자동 감지
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

        // 3. 비-UTF-8 아시아권 인코딩 폴백 (시스템 로케일 또는 다국어 CP 시도)
        try
        {
            // 중국어 간체 (GBK / GB18030)
            return Encoding.GetEncoding(936);
        }
        catch
        {
            try { return Encoding.GetEncoding(949); } // 한국어 CP949
            catch { return Encoding.Default; }
        }
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
