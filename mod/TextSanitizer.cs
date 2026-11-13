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

    private static readonly char[] s_specialQuotes = { '’', '‘', '`', '´', '＇' };

    /// <summary>
    /// 문자열의 공백 및 따옴표를 정규화한다. 정상 문자열은 추가 할당 없이 그대로 돌려준다.
    /// </summary>
    public static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;

        var text = input;

        // 1. 유니코드 특수 따옴표가 있을 때만 표준 아포스트로피(')로 통일
        if (text.IndexOfAny(s_specialQuotes) >= 0)
        {
            text = text
                .Replace('’', '\'')
                .Replace('‘', '\'')
                .Replace('`', '\'')
                .Replace('´', '\'')
                .Replace('＇', '\'');
        }

        // 2~3. 아포스트로피 주변에 공백이 있을 때만 영문 축약형/단어 사이 공백 교정
        if (HasSpaceAroundApostrophe(text))
        {
            text = s_contractionRegex.Replace(text, "$1'$2");
            text = s_generalApostropheRegex.Replace(text, "$1'$2");
        }

        // 4. 앞뒤·연속·특수 공백이 있을 때만 정규화
        if (NeedsSpaceNormalization(text))
            text = s_multipleSpacesRegex.Replace(text, " ").Trim();

        return text;
    }

    private static bool HasSpaceAroundApostrophe(string s)
    {
        int idx = 0;
        while ((idx = s.IndexOf('\'', idx)) >= 0)
        {
            if ((idx > 0 && char.IsWhiteSpace(s[idx - 1])) ||
                (idx + 1 < s.Length && char.IsWhiteSpace(s[idx + 1])))
                return true;
            idx++;
        }
        return false;
    }

    private static bool NeedsSpaceNormalization(string s)
    {
        if (s.Length == 0) return false;
        if (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1])) return true;
        bool prevSpace = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == ' ')
            {
                if (prevSpace) return true;
                prevSpace = true;
            }
            else if (char.IsWhiteSpace(c) || c == '\u200B')
            {
                return true;
            }
            else
            {
                prevSpace = false;
            }
        }
        return false;
    }
}
