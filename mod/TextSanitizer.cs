using System.Text.RegularExpressions;

namespace PLATiNALAB.Mod;

/// <summary>
/// 영어, 한국어, CJK 텍스트의 비정상 공백 및 특수 문장 부호를 정규화하는 헬퍼.
/// 특히 영어 축약형에서 아포스트로피 전후로 띄어쓰기가 비정상적으로 벌어지는 현상
/// (예: "you'  re" -> "you're", "don ' t" -> "don't", "it ' s" -> "it's") 및
/// 유니코드 특수 따옴표/아포스트로피(’, ‘, ʼ, ´, ＇), 연속 공백, 특수 공백(NBSP 등)을 완벽히 교정한다.
/// </summary>
internal static class TextSanitizer
{
    // 영문 아포스트로피 전후의 비정상 공백을 탐지하는 정규식
    // 1. 단어 + 공백 + 아포스트로피 + 공백 + 축약형 접미사 (re, t, s, m, ll, ve, d)
    private static readonly Regex s_contractionRegex = new(
        @"\b([a-zA-Z]+)\s*['’`´＇]\s*(re|t|s|m|ll|ve|d)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 일반 영단어 사이의 아포스트로피 공백 교정 (예: o ' clock -> o'clock)
    private static readonly Regex s_generalApostropheRegex = new(
        @"\b([a-zA-Z]+)\s*['’`´＇]\s*([a-zA-Z]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 2개 이상의 연속된 공백 및 유니코드 특수 공백(NBSP \u00A0, 전각 공백 \u3000 등)
    private static readonly Regex s_multipleSpacesRegex = new(
        @"[\u00A0\u1680\u2000-\u200B\u202F\u205F\u3000\s]+",
        RegexOptions.Compiled);

    /// <summary>
    /// 문자열의 공백 및 따옴표를 정규화한다.
    /// </summary>
    public static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;

        // 1. 유니코드 특수 따옴표를 표준 아포스트로피(')로 통일
        var text = input
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Replace('`', '\'')
            .Replace('´', '\'')
            .Replace('＇', '\'');

        // 2. 영문 축약형 아포스트로피 띄어쓰기 교정 (you'  re -> you're, don ' t -> don't 등)
        text = s_contractionRegex.Replace(text, "$1'$2");

        // 3. 단어 사이 아포스트로피 전후 불필요한 공백 제거
        text = s_generalApostropheRegex.Replace(text, "$1'$2");

        // 4. 연속 공백 및 특수 공백을 단일 공백으로 치환
        text = s_multipleSpacesRegex.Replace(text, " ");

        return text.Trim();
    }
}
