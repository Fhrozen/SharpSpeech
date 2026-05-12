# Text Sanitization for TTS Processing

## Problem
When users paste markdown text or content with special characters (bullets, parentheses, unicode symbols) into the TTS interface, the espeak-ng phonemization library can crash with `free(): invalid pointer` error, causing the entire service to terminate.

## Root Cause
The espeak-ng C library (libespeak-ng.so.1) is sensitive to certain special characters and formatting:
- Unicode bullet points (•, ‣, ⁃, ◦, ▪, ▫)
- Markdown formatting (* for bold, _ for italic, ` for code)
- Parentheses and brackets in certain contexts
- Zero-width unicode characters (ZWSP, ZWNJ, ZWJ, BOM)
- Non-ASCII characters passed to the phonemization engine

## Solution
Implemented **two-layer text sanitization**:

### Layer 1: API-Level Sanitization ([TextSanitizer.cs](../src/FastTTSR.Api/Services/TextSanitizer.cs))
Applied in `Program.cs` before TTS synthesis:

**Cleaning Operations:**
1. Remove markdown bullets (`•`, `-`, `*`, `+` at line starts)
2. Remove markdown headers (`#`, `##`, `###`, etc.)
3. Convert markdown links `[text](url)` → `text`
4. Remove markdown formatting (`**bold**`, `*italic*`, `` `code` ``, `~~strike~~`)
5. Replace parentheses `()` with spaces (preserves sentence structure)
6. Remove unicode special characters (bullets, zero-width chars, non-breaking spaces)
7. Normalize excessive whitespace (multiple spaces → single space)
8. Limit consecutive newlines (max 2 for one blank line)

### Layer 2: Espeak-Level Sanitization ([EspeakWrapper.cs](../src/FastTTSR.Api/Services/Phonemization/EspeakWrapper.cs))
Applied immediately before calling espeak_TextToPhonemes:

**Safety Measures:**
1. Preserve UTF-8 text (including CJK scripts) while removing unsafe control characters
2. Normalize whitespace before word-level processing
3. Wrap espeak P/Invoke call in try-catch to gracefully handle failures
4. Skip problematic words rather than crashing the entire process

## Usage
Text sanitization is **automatic and transparent**. No changes required to API calls:

```bash
curl -X POST http://localhost:5768/v1/audio/speech \
  -H "Content-Type: application/json" \
  -d '{
    "model": "kokoro-q4",
    "input": "• First item\n• Second item\n• Use (English) format",
    "voice": "af_bella",
    "speed": 1.0
  }'
```

**Before Sanitization:**
```
• First item
• Second item
• Use (English) format
```

**After Sanitization:**
```
First item
Second item
Use English format
```

## Testing
Comprehensive unit tests are available in [TextSanitizerTests.cs](../tests/FastTTSR.Api.Tests/TextSanitizerTests.cs):

- Markdown bullets removal
- Markdown headers removal
- Markdown link conversion
- Markdown formatting removal
- Unicode character filtering
- Whitespace normalization
- Newline limiting
- Parentheses handling
- Complex document processing

Run tests:
```bash
cd /export/db/FastTTSR
dotnet test tests/FastTTSR.Api.Tests/FastTTSR.Api.Tests.csproj --filter TextSanitizerTests
```

## Implementation Details

### Files Modified
1. **src/FastTTSR.Api/Services/TextSanitizer.cs** (new)
   - Static utility class with regex-based text cleaning
   - Comprehensive special character filtering
   - Preserves natural language readability

2. **src/FastTTSR.Api/Program.cs**
   - Integrated sanitizer in `/v1/audio/speech` endpoint
   - Validates sanitized output is not empty
   - Returns 400 Bad Request if only unsupported chars

3. **src/FastTTSR.Api/Services/Phonemization/EspeakWrapper.cs**
   - Added non-ASCII character stripping before espeak calls
   - Wrapped espeak_TextToPhonemes in try-catch
   - Added error logging for debugging

4. **tests/FastTTSR.Api.Tests/TextSanitizerTests.cs** (new)
   - 20+ unit tests covering all sanitization scenarios
   - Edge case validation
   - Regression test suite

### Performance Impact
- **Minimal overhead**: Regex operations are compiled and cached
- **Typical processing time**: <1ms for standard text inputs
- **Memory**: StringBuilder used for efficient character filtering
- **No blocking**: All operations are synchronous and fast

## Known Limitations
1. **Unicode Text**: Non-English scripts are preserved and passed to espeak-ng as UTF-8
   - Use the `language` parameter to select the correct language/voice mapping for best quality

2. **Parenthetical Information**: Content in parentheses is replaced with spaces
   - **Example**: "Use (English)" → "Use  English"
   - **Rationale**: Preserves sentence structure better than complete removal

3. **Markdown Lists**: List structure is lost (bullets removed)
   - **Example**: Markdown lists become paragraph text
   - **Acceptable**: TTS reads content naturally without visual formatting

## Future Enhancements
1. **Language-Aware Sanitization**: Expand locale-specific punctuation rules and normalization
2. **Configurable Sanitization Levels**: Allow users to control aggressiveness
3. **Sanitization Metrics**: Track what was removed for user feedback
4. **Pre-sanitization Preview**: Show users cleaned text before synthesis

## Troubleshooting

### Symptom: Empty Audio or 400 Error
**Cause**: Input contains only unsupported characters  
**Solution**: Check that input has actual text content, not just formatting

### Symptom: Espeak Still Crashes
**Cause**: New special character not in filter list  
**Solution**: Add character to `ProblematicChars` HashSet in TextSanitizer.cs

### Symptom: Text Sounds Wrong
**Cause**: Over-aggressive sanitization removed important words  
**Solution**: Review `TextSanitizer.Sanitize()` regex patterns and adjust

## References
- [espeak-ng Documentation](https://github.com/espeak-ng/espeak-ng)
- [ONNX Kokoro TTS Model](https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX)
- [Unicode Character Categories](https://www.unicode.org/reports/tr44/#General_Category_Values)
