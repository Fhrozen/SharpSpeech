using FastTTSR.Api.Services;
using Xunit;

namespace FastTTSR.Api.Tests;

public class TextSanitizerTests
{
    [Fact]
    public void Sanitize_RemovesMarkdownBullets()
    {
        // Arrange
        var input = @"• First item
- Second item
* Third item
+ Fourth item";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        Assert.DoesNotContain("•", result);
        Assert.DoesNotContain("-", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("+", result);
        Assert.Contains("First item", result);
        Assert.Contains("Second item", result);
        Assert.Contains("Third item", result);
        Assert.Contains("Fourth item", result);
    }

    [Fact]
    public void Sanitize_RemovesMarkdownHeaders()
    {
        // Arrange
        var input = @"# Header 1
## Header 2
### Header 3";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        Assert.DoesNotContain("#", result);
        Assert.Contains("Header 1", result);
        Assert.Contains("Header 2", result);
        Assert.Contains("Header 3", result);
    }

    [Fact]
    public void Sanitize_ConvertsMarkdownLinks()
    {
        // Arrange
        var input = "Check out [GitHub](https://github.com) for code.";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        Assert.DoesNotContain("](", result);
        Assert.DoesNotContain("https://", result);
        Assert.Contains("GitHub", result);
        Assert.Contains("Check out", result);
        Assert.Contains("for code", result);
    }

    [Fact]
    public void Sanitize_RemovesMarkdownFormatting()
    {
        // Arrange
        var input = "This is **bold**, *italic*, `code`, and ~~strikethrough~~.";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        Assert.DoesNotContain("**", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("`", result);
        Assert.DoesNotContain("~~", result);
        Assert.Contains("bold", result);
        Assert.Contains("italic", result);
        Assert.Contains("code", result);
        Assert.Contains("strikethrough", result);
    }

    [Fact]
    public void Sanitize_RemovesProblematicUnicodeCharacters()
    {
        // Arrange
        var input = "Text\u2022with\u00A0special\u200Bchars\uFEFF";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        Assert.DoesNotContain('\u2022', result); // bullet
        Assert.DoesNotContain('\u00A0', result); // non-breaking space
        Assert.DoesNotContain('\u200B', result); // zero-width space
        Assert.DoesNotContain('\uFEFF', result); // BOM
        Assert.Contains("Text", result);
        Assert.Contains("with", result);
        Assert.Contains("special", result);
        Assert.Contains("chars", result);
    }

    [Fact]
    public void Sanitize_NormalizesWhitespace()
    {
        // Arrange
        var input = "Too    many     spaces";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        Assert.Equal("Too many spaces", result);
    }

    [Fact]
    public void Sanitize_LimitsConsecutiveNewlines()
    {
        // Arrange
        var input = "Line 1\n\n\n\n\nLine 2";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        Assert.DoesNotContain("\n\n\n", result);
        Assert.Contains("Line 1", result);
        Assert.Contains("Line 2", result);
    }

    [Fact]
    public void Sanitize_TrimsWhitespace()
    {
        // Arrange
        var input = "   Text with spaces   ";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        Assert.Equal("Text with spaces", result);
    }

    [Fact]
    public void Sanitize_HandlesEmptyString()
    {
        // Arrange
        var input = "";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Sanitize_HandlesNullString()
    {
        // Act
        var result = TextSanitizer.Sanitize(null!);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Sanitize_HandlesComplexMarkdownDocument()
    {
        // Arrange
        var input = @"# Main Title

## Features

- Feature one with **bold** text
- Feature two with *italic* text
- Feature three with `code`

Check out the [documentation](https://example.com) for more details.

### Installation

1. First step
2. Second step
3. Third step";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        // Should not contain markdown syntax
        Assert.DoesNotContain("#", result);
        Assert.DoesNotContain("-", result);
        Assert.DoesNotContain("**", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("`", result);
        Assert.DoesNotContain("[", result);
        Assert.DoesNotContain("](", result);
        
        // Should contain cleaned text content
        Assert.Contains("Main Title", result);
        Assert.Contains("Features", result);
        Assert.Contains("Feature one", result);
        Assert.Contains("bold", result);
        Assert.Contains("italic", result);
        Assert.Contains("code", result);
        Assert.Contains("documentation", result);
        Assert.Contains("Installation", result);
    }

    [Fact]
    public void NeedsSanitization_DetectsMarkdownBullets()
    {
        // Arrange
        var input = "- Item";

        // Act
        var result = TextSanitizer.NeedsSanitization(input);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void NeedsSanitization_DetectsMarkdownHeaders()
    {
        // Arrange
        var input = "# Header";

        // Act
        var result = TextSanitizer.NeedsSanitization(input);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void NeedsSanitization_DetectsMarkdownLinks()
    {
        // Arrange
        var input = "[text](url)";

        // Act
        var result = TextSanitizer.NeedsSanitization(input);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void NeedsSanitization_DetectsProblematicCharacters()
    {
        // Arrange
        var input = "Text\u2022with bullet";

        // Act
        var result = TextSanitizer.NeedsSanitization(input);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void NeedsSanitization_ReturnsFalseForCleanText()
    {
        // Arrange
        var input = "This is clean text.";

        // Act
        var result = TextSanitizer.NeedsSanitization(input);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Sanitize_PreservesRegularPunctuation()
    {
        // Arrange
        var input = "Hello, world! How are you? I'm fine.";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void Sanitize_PreservesNumbersAndDates()
    {
        // Arrange
        var input = "Meeting on 2024-01-15 at 3:30 PM. Cost: $50.00";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        Assert.Contains("2024-01-15", result);
        Assert.Contains("3:30", result);
        Assert.Contains("$50.00", result);
    }

    [Fact]
    public void Sanitize_PreservesJapaneseText()
    {
        // Japanese text with hiragana and katakana (no punctuation)
        var input = "こんにちは 世界 これは 日本語の テストです";

        var result = TextSanitizer.Sanitize(input, "ja-jp");

        // Japanese characters should be preserved
        Assert.Contains("こんにちは", result);
        Assert.Contains("世界", result);
        Assert.Contains("テスト", result);
    }

    [Fact]
    public void Sanitize_NormalizesCjkPunctuation()
    {
        // Arrange - Japanese/Chinese punctuation
        var input = "こんにちは、世界！これは日本語のテストです。";

        // Act
        var result = TextSanitizer.Sanitize(input, "ja-jp");

        // Assert - CJK punctuation should be converted to ASCII
        Assert.DoesNotContain("、", result);  // Japanese comma should be removed
        Assert.DoesNotContain("。", result);  // Japanese period should be removed
        Assert.DoesNotContain("！", result);  // Japanese exclamation should be removed
        Assert.Contains(",", result);   // Converted to ASCII comma
        Assert.Contains(".", result);   // Converted to ASCII period
        Assert.Contains("!", result);   // Converted to ASCII exclamation
        // Japanese characters should still be present
        Assert.Contains("こんにちは", result);
        Assert.Contains("世界", result);
    }

    [Fact]
    public void Sanitize_NormalizesChinesePunctuation()
    {
        // Arrange - Chinese punctuation
        var input = "你好，世界！这是中文测试？";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert - Chinese punctuation should be converted to ASCII
        Assert.DoesNotContain("，", result);  // Chinese comma
        Assert.DoesNotContain("！", result);  // Chinese exclamation
        Assert.DoesNotContain("？", result);  // Chinese question mark
        Assert.Contains(",", result);
        Assert.Contains("!", result);
        Assert.Contains("?", result);
    }

    [Fact]
    public void Sanitize_NormalizesCjkColonsSemicolons()
    {
        // Arrange - Japanese/Chinese colons and semicolons
        var input = "注意：这很重要；请阅读。";

        // Act
        var result = TextSanitizer.Sanitize(input);

        // Assert
        Assert.DoesNotContain("：", result);  // Full-width colon
        Assert.DoesNotContain("；", result);  // Full-width semicolon
        Assert.Contains(":", result);
        Assert.Contains(";", result);
    }
}
