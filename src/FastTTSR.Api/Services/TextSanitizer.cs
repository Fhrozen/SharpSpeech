using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace FastTTSR.Api.Services;

/// <summary>
/// Sanitizes text input for TTS processing by removing problematic characters
/// and markdown formatting that can cause TTS engines to crash or misbehave.
/// </summary>
public static class TextSanitizer
{
    // Regex patterns for cleaning
    private static readonly Regex MarkdownBullets = new(@"^[\s]*[•\-\*\+]\s*", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MarkdownHeaders = new(@"^[\s]*#{1,6}\s+", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MarkdownLinks = new(@"\[([^\]]+)\]\([^\)]+\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownFormatting = new(@"[\*_`~]{1,3}([^\*_`~]+)[\*_`~]{1,3}", RegexOptions.Compiled);
    private static readonly Regex ExcessiveWhitespace = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex ExcessiveNewlines = new(@"\n{3,}", RegexOptions.Compiled);
    
    // Characters that commonly cause TTS engine crashes
    private static readonly HashSet<char> ProblematicChars = new()
    {
        '\u2022', // bullet •
        '\u2023', // triangular bullet ‣
        '\u2043', // hyphen bullet ⁃
        '\u25E6', // white bullet ◦
        '\u25AA', // black small square ▪
        '\u25AB', // white small square ▫
        '\u00A0', // non-breaking space
        '\u200B', // zero-width space
        '\u200C', // zero-width non-joiner
        '\u200D', // zero-width joiner
        '\uFEFF', // zero-width no-break space (BOM)
        // Note: Parentheses handled separately to preserve sentence structure
    };

    /// <summary>
    /// Sanitizes text for TTS processing by removing markdown formatting,
    /// special characters, and excessive whitespace.
    /// </summary>
    /// <param name="text">The raw input text</param>
    /// <param name="language">Optional language hint for future language-aware sanitization</param>
    /// <returns>Sanitized text safe for TTS processing</returns>
    public static string Sanitize(string text, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sanitized = text;

        // CRITICAL: Handle CJK punctuation FIRST - these cause espeak-ng crashes
        // Convert Japanese/Chinese punctuation to ASCII equivalents before any other processing
        // Reference: https://github.com/remsky/Kokoro-FastAPI (normalizer.py)
        sanitized = sanitized.Replace("、", ", ");   // Japanese comma
        sanitized = sanitized.Replace("。", ". ");   // Japanese period
        sanitized = sanitized.Replace("！", "! ");   // Japanese/Chinese exclamation
        sanitized = sanitized.Replace("，", ", ");   // Chinese comma
        sanitized = sanitized.Replace("：", ": ");   // Japanese/Chinese colon
        sanitized = sanitized.Replace("；", "; ");   // Japanese/Chinese semicolon
        sanitized = sanitized.Replace("？", "? ");   // Japanese/Chinese question mark
        sanitized = sanitized.Replace("–", "- ");    // en-dash
        sanitized = sanitized.Replace("—", "- ");    // em-dash

        // Remove markdown bullets (•, -, *, + at line starts)
        sanitized = MarkdownBullets.Replace(sanitized, string.Empty);

        // Remove markdown headers (###)
        sanitized = MarkdownHeaders.Replace(sanitized, string.Empty);

        // Convert markdown links [text](url) to just text
        sanitized = MarkdownLinks.Replace(sanitized, "$1");

        // Remove markdown formatting (*bold*, _italic_, `code`, ~~strikethrough~~)
        sanitized = MarkdownFormatting.Replace(sanitized, "$1");

        // Replace parentheses with spaces (removing content causes sentence structure issues)
        sanitized = sanitized.Replace("(", " ").Replace(")", " ");

        // Remove problematic unicode characters (except parentheses which we already handled)
        var sb = new StringBuilder(sanitized.Length);
        foreach (var ch in sanitized)
        {
            var category = char.GetUnicodeCategory(ch);
            if ((char.IsControl(ch) || category == UnicodeCategory.Format) && ch is not '\n' and not '\r' and not '\t')
            {
                continue;
            }

            if (!ProblematicChars.Contains(ch))
            {
                sb.Append(ch);
            }
        }
        sanitized = sb.ToString();

        // Normalize whitespace (replace multiple spaces with single space)
        sanitized = ExcessiveWhitespace.Replace(sanitized, " ");

        // Limit consecutive newlines to 2 (one blank line)
        sanitized = ExcessiveNewlines.Replace(sanitized, "\n\n");

        // Trim leading/trailing whitespace
        sanitized = sanitized.Trim();

        return sanitized;
    }

    /// <summary>
    /// Validates if text contains problematic characters that should be sanitized.
    /// </summary>
    /// <param name="text">The text to validate</param>
    /// <returns>True if text needs sanitization, false otherwise</returns>
    public static bool NeedsSanitization(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Check for markdown patterns
        if (MarkdownBullets.IsMatch(text) || 
            MarkdownHeaders.IsMatch(text) || 
            MarkdownLinks.IsMatch(text) || 
            MarkdownFormatting.IsMatch(text))
        {
            return true;
        }

        // Check for problematic characters
        foreach (var ch in text)
        {
            if (ProblematicChars.Contains(ch))
            {
                return true;
            }
        }

        // Check for excessive whitespace
        if (ExcessiveWhitespace.IsMatch(text) || ExcessiveNewlines.IsMatch(text))
        {
            return true;
        }

        return false;
    }
}
