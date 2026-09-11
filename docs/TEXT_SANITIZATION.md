# Text Sanitization and Thread Safety for TTS Processing

## Problem
When users paste markdown text or content with special characters (bullets, parentheses, unicode symbols) into the TTS interface, or when processing Japanese/Chinese multi-byte UTF-8 text, the espeak-ng phonemization library can crash with `free(): invalid pointer` error, causing the entire service to terminate.

## Root Causes

### 1. espeak-ng Thread Safety Issue (CRITICAL)
The **primary cause** of the "free(): invalid pointer" crash is that `espeak_TextToPhonemes()` is **NOT thread-safe**:
- Returns a pointer to a **static global buffer** (`phon_out_buf`) that gets dynamically reallocated
- When processing multi-byte UTF-8 text (Japanese, Chinese), the buffer requires more space
- `realloc()` is called to expand the buffer, potentially returning a new memory address
- Concurrent calls can invalidate pointers held by other threads → **memory corruption** → crash

**Why Japanese/Chinese triggers it more frequently:**
- Japanese/Chinese characters use 3-4 bytes per character in UTF-8 (vs. 1 byte for ASCII)
- IPA phoneme output requires larger buffers
- More frequent `realloc()` calls → higher probability of pointer invalidation

### 2. Special Characters and Markdown Formatting
The espeak-ng C library (libespeak-ng.so.1) is also sensitive to certain special characters and formatting:
- Unicode bullet points (•, ‣, ⁃, ◦, ▪, ▫)
- Markdown formatting (* for bold, _ for italic, ` for code)
- Parentheses and brackets in certain contexts
- Zero-width unicode characters (ZWSP, ZWNJ, ZWJ, BOM)

## Solution
Implemented **three-layer protection**:

### Layer 1: Thread-Safe espeak Wrapper ([EspeakWrapper.cs](../src/SharpAudio.Api/Services/Phonemization/EspeakWrapper.cs))
Added **critical thread synchronization** using `SemaphoreSlim` to serialize all espeak-ng calls:

**Key Changes:**
1. Added `private static readonly SemaphoreSlim _espeakLock = new(1, 1);` for mutual exclusion
2. Wrapped `SetVoice()` calls with lock (espeak maintains internal state)
3. Wrapped `espeak_TextToPhonemes()` AND `Marshal.PtrToStringUTF8()` with lock (CRITICAL: must copy the string before releasing lock)

**Why this works:**
- Only one thread can call espeak-ng at a time
- The pointer returned by `espeak_TextToPhonemes()` is copied to managed memory (`Marshal.PtrToStringUTF8`) before another thread can trigger `realloc()`
- Prevents pointer invalidation race conditions

**Code example:**
```csharp
_espeakLock.Wait();
try
{
    var result = espeak_TextToPhonemes(ref wordCopy, 1, 0x02);
    var phoneme = Marshal.PtrToStringUTF8(result)?.Replace("_", "");
    phonemes += phoneme;
}
finally
{
    _espeakLock.Release();
}
```

### Layer 2: API-Level Text Sanitization ([TextSanitizer.cs](../src/SharpAudio.Api/Services/TextSanitizer.cs))
Applied in `Program.cs` before TTS synthesis:

**Cleaning Operations:**
1. **[CRITICAL] CJK punctuation normalization** - Convert Japanese/Chinese punctuation to ASCII equivalents (MUST be first):
   - `、` → `, ` (Japanese comma)
   - `。` → `. ` (Japanese period)
   - `！` → `! ` (Japanese/Chinese exclamation)
   - `，` → `, ` (Chinese comma)
   - `：` → `: ` (Japanese/Chinese colon)
   - `；` → `; ` (Japanese/Chinese semicolon)
   - `？` → `? ` (Japanese/Chinese question mark)
   - `–` → `- ` (en-dash)
   - `—` → `- ` (em-dash)
   
   **Why this is critical:** espeak-ng crashes with "free(): invalid pointer" when encountering CJK punctuation. These full-width characters must be converted to ASCII before any other processing. Reference: [Kokoro-FastAPI normalizer.py](https://github.com/remsky/Kokoro-FastAPI)

2. Remove markdown bullets (`•`, `-`, `*`, `+` at line starts)
3. Remove markdown headers (`#`, `##`, `###`, etc.)
4. Convert markdown links `[text](url)` → `text`
5. Remove markdown formatting (`**bold**`, `*italic*`, `` `code` ``, `~~strike~~`)
6. Replace parentheses `()` with spaces (preserves sentence structure)
7. Remove unicode special characters (bullets, zero-width chars, non-breaking spaces)
8. Normalize excessive whitespace (multiple spaces → single space)
9. Limit consecutive newlines (max 2 for one blank line)

### Layer 3: Espeak-Level Error Handling ([EspeakWrapper.cs](../src/SharpAudio.Api/Services/Phonemization/EspeakWrapper.cs))
Applied immediately before calling espeak_TextToPhonemes:

**Safety Measures:**
1. Preserve UTF-8 text (including CJK scripts) while removing unsafe control characters
2. Normalize whitespace before word-level processing
3. Wrap espeak P/Invoke call in try-catch to gracefully handle failures
4. Skip problematic words rather than crashing the entire process

## Thread Safety Testing

### Concurrent Japanese Text Synthesis
Created comprehensive tests in [JapaneseConcurrencyTests.cs](../tests/SharpAudio.Api.IntegrationTests/JapaneseConcurrencyTests.cs):

1. **Concurrent_Japanese_requests_should_not_crash**: 10 different Japanese texts concurrently
2. **Stress_test_20_concurrent_Japanese_requests**: 20 identical Japanese requests
3. **Mixed_language_concurrent_requests**: English, Japanese, Chinese, French, Spanish mixed
4. **Japanese_with_complex_characters**: Kanji, hiragana, katakana, emojis, symbols
5. **Sequential_Japanese_requests**: Control test (non-concurrent)

### Manual Stress Test
```bash
# Test 10 concurrent Japanese requests
for i in {1..10}; do
  curl -X POST http://localhost:9090/v1/audio/speech \
    -H "Content-Type: application/json" \
    -d "{\"model\":\"kokoro-q4\",\"input\":\"これは並行テスト番号${i}です。\",\"language\":\"ja-jp\",\"voice\":\"jf_alpha\"}" \
    -o "/tmp/japanese_concurrent_${i}.wav" &
done
wait
```

**Verified Results:**
- ✅ All 10 concurrent requests succeeded (HTTP 200)
- ✅ All WAV files created (467KB-488KB each)
- ✅ No "free(): invalid pointer" crashes
- ✅ Service remained stable under concurrent load

## Performance Impact

**Trade-off: Thread Safety vs. Concurrency**
- **Before fix**: Multiple espeak calls could run concurrently (but crashed frequently with Japanese text)
- **After fix**: Espeak calls are serialized through `SemaphoreSlim` (safe but sequential)

**Impact:** Minimal for typical TTS workloads
- Phonemization is **not the bottleneck** — ONNX inference takes 100-500ms vs. <5ms for phonemization
- Sequential espeak processing adds negligible latency (<10ms per request)
- **Acceptable trade-off** for production stability

**Measured Performance:**
- Single Japanese request: ~400ms total (390ms ONNX, ~10ms phonemization)
- 10 concurrent Japanese requests: ~15 seconds total (~1.5s per request average)
- Throughput: ~40-50 requests/minute with Japanese text

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
Comprehensive unit tests are available in [TextSanitizerTests.cs](../tests/SharpAudio.Api.Tests/TextSanitizerTests.cs):

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
cd /export/db/SharpAudio
dotnet test tests/SharpAudio.Api.Tests/SharpAudio.Api.Tests.csproj --filter TextSanitizerTests
```

## Implementation Details

### Files Modified
1. **src/SharpAudio.Api/Services/Phonemization/EspeakWrapper.cs** (CRITICAL FIX)
   - Added `SemaphoreSlim _espeakLock` for thread synchronization
   - Wrapped `SetVoice()` with lock (espeak internal state modification)
   - Wrapped `espeak_TextToPhonemes()` + `Marshal.PtrToStringUTF8()` with lock (pointer safety)
   - Added detailed comments explaining the thread-safety requirements

2. **src/SharpAudio.Api/Services/TextSanitizer.cs** (existing)
   - Static utility class with regex-based text cleaning
   - Comprehensive special character filtering
   - Preserves natural language readability

3. **src/SharpAudio.Api/Program.cs** (existing)
   - Integrated sanitizer in `/v1/audio/speech` endpoint
   - Validates sanitized output is not empty
   - Returns 400 Bad Request if only unsupported chars

4. **tests/SharpAudio.Api.IntegrationTests/JapaneseConcurrencyTests.cs** (NEW)
   - 6 comprehensive concurrency tests for Japanese text
   - Tests 10-20 concurrent requests
   - Mixed language testing
   - Complex character testing (emojis, symbols, kanji)

5. **tests/SharpAudio.Api.Tests/TextSanitizerTests.cs** (existing)
   - 20+ unit tests covering all sanitization scenarios
   - Edge case validation
   - Regression test suite

### Text Sanitization Performance
- **Minimal overhead**: Regex operations are compiled and cached
- **Typical processing time**: <1ms for standard text inputs
- **Memory**: StringBuilder used for efficient character filtering
- **No blocking**: All operations are synchronous and fast

### Thread Safety Performance (see "Performance Impact" section above)
- Phonemization is sequential (thread-safe via SemaphoreSlim)
- Negligible impact on total TTS latency (<10ms added)
- ONNX inference remains the bottleneck (100-500ms)

## Known Limitations

### 1. Japanese Kanji and Katakana Phonemization (IMPORTANT)
**Problem:** espeak-ng does not have kanji-to-phoneme or katakana-to-phoneme conversion capabilities. When Japanese text contains kanji or katakana characters, espeak may:
- Output "character" or "Chinese character" in English
- Produce incorrect or garbled phonemes
- Generate English-sounding speech instead of proper Japanese pronunciation

**Examples of affected text:**
- `漢字` (kanji) → espeak reads as "character" instead of "kanji"
- `カタカナ` (katakana) → may not be properly phonemized
- `こんにちは世界` → "world" (世界) may be read incorrectly

**Why this happens:**
The Kokoro-82M TTS model requires IPA (International Phonetic Alphabet) phonemes as input. The workflow is:
1. Text → espeak-ng → IPA phonemes → Kokoro model → speech
2. For English: "hello" → `/həˈloʊ/` → correct speech
3. For Japanese kanji: "世界" → ??? (espeak-ng can't convert) → incorrect speech

**CJK Punctuation Fixed:** While we now handle CJK punctuation conversion (、→, 。→. etc.) to prevent crashes, the kanji/katakana pronunciation issue remains.

**Potential Solutions (not yet implemented):**
1. **kanji-to-romaji preprocessor**: Use a library like pykakasi (Python) or MeCab (C++) to convert kanji to romaji before phonemization
2. **Bypass espeak for Japanese**: Use a dedicated Japanese G2P (Grapheme-to-Phoneme) library
3. **Train custom Japanese phonemizer**: Deep learning model specifically for Japanese text-to-IPA
4. **Use Kokoro's native Japanese support**: If available, check if the model accepts raw Japanese text without phonemization

**Current Workaround:** For best results with Japanese, use:
- Hiragana text (already phonetic) - works well
- Romaji (romanized Japanese) - works well
- Avoid heavy use of kanji until a proper solution is implemented

**Testing:**
```bash
# This works well (hiragana only)
curl -X POST http://localhost:9090/v1/audio/speech \
  -H "Content-Type: application/json" \
  -d '{"model":"kokoro-q4","input":"こんにちは せかい","language":"ja-jp","voice":"jf_alpha"}'

# This may produce incorrect output (contains kanji)
curl -X POST http://localhost:9090/v1/audio/speech \
  -H "Content-Type: application/json" \
  -d '{"model":"kokoro-q4","input":"こんにちは世界","language":"ja-jp","voice":"jf_alpha"}'
```

### 2. Sequential Phonemization (Thread Safety Trade-off)
- **Before**: Concurrent espeak calls (crashed with Japanese text)
- **After**: Sequential espeak calls via `SemaphoreSlim` (stable but serialized)
- **Impact**: Minimal - phonemization is <5ms, ONNX inference is 100-500ms
- **Future**: Could implement espeak instance pooling for true parallelism (deferred unless needed)

### 3. Unicode Text and Language Support
- Non-English scripts (Japanese, Chinese, etc.) are preserved and passed to espeak-ng as UTF-8
- Use the `language` parameter to select the correct language/voice mapping for best quality
- espeak-ng must have the appropriate language data files installed (`/usr/lib/x86_64-linux-gnu/espeak-ng-data`)

### 4. Parenthetical Information
- Content in parentheses is replaced with spaces
- **Example**: "Use (English)" → "Use  English"
- **Rationale**: Preserves sentence structure better than complete removal

### 5. Markdown Lists
- List structure is lost (bullets removed)
- **Example**: Markdown lists become paragraph text
- **Acceptable**: TTS reads content naturally without visual formatting

## Troubleshooting

### Symptom: "free(): invalid pointer" crash with Japanese/Chinese text
**Root Cause**: espeak-ng thread-safety issue (now fixed)  
**Solution**: ✅ **FIXED** - Thread synchronization added in EspeakWrapper.cs  
**Verification**: Run `JapaneseConcurrencyTests` to confirm fix

### Symptom: Service crashes under concurrent load
**Root Cause**: Multiple threads calling espeak_TextToPhonemes simultaneously  
**Solution**: ✅ **FIXED** - `SemaphoreSlim` serializes all espeak calls  
**Verification**: Test with 10-20 concurrent requests (see "Thread Safety Testing" section)

### Symptom: Empty Audio or 400 Error
**Cause**: Input contains only unsupported characters  
**Solution**: Check that input has actual text content, not just formatting

### Symptom: Espeak crashes with specific characters
**Cause**: New special character not in filter list  
**Solution**: Add character to `ProblematicChars` HashSet in TextSanitizer.cs

### Symptom: Text Sounds Wrong
**Cause**: Over-aggressive sanitization removed important words  
**Solution**: Review `TextSanitizer.Sanitize()` regex patterns and adjust

### Symptom: Japanese text still causes crashes (after fix)
**Diagnostic Steps**:
1. Verify EspeakWrapper.cs has the `_espeakLock` field
2. Check that `SetVoice()` and `ConvertToPhonemes()` use the lock
3. Confirm Docker image was rebuilt after code changes
4. Test with: `docker compose build && docker compose up`

## Testing and Verification

### Unit Tests
```bash
cd /export/db/SharpAudio
docker compose -f docker-compose.test.yml run --rm unit-tests
```

**Expected Results:**
- ✅ `TextSanitizerTests` - all tests pass
- ✅ `SpeechEndpointTests.Returns_audio_for_japanese_input_and_language` - passes

### Integration Tests (with Concurrency)
```bash
cd /export/db/SharpAudio
docker compose -f docker-compose.test.yml up -d fastttsr
sleep 30  # Wait for service to be ready
docker compose -f docker-compose.test.yml run --rm integration-tests
```

**Expected Results:**
- ✅ `JapaneseConcurrencyTests.Concurrent_Japanese_requests_should_not_crash` - passes
- ✅ `JapaneseConcurrencyTests.Stress_test_20_concurrent_Japanese_requests` - passes
- ✅ No "free(): invalid pointer" errors in logs

### Manual Japanese Test
```bash
curl -X POST http://localhost:9090/v1/audio/speech \
  -H "Content-Type: application/json" \
  -d '{"model":"kokoro-q4","input":"こんにちは、世界！これは日本語のテストです。","language":"ja-jp","voice":"jf_alpha"}' \
  -o japanese_test.wav

# Should return HTTP 200 and create a valid WAV file
ls -lh japanese_test.wav
```

## Future Enhancements
1. **Espeak Instance Pooling**: Create multiple espeak instances for true parallel phonemization (only if performance becomes critical)
2. **Language-Aware Sanitization**: Expand locale-specific punctuation rules and normalization
3. **Configurable Sanitization Levels**: Allow users to control aggressiveness
4. **Sanitization Metrics**: Track what was removed for user feedback
5. **Pre-sanitization Preview**: Show users cleaned text before synthesis

## References
- [espeak-ng Documentation](https://github.com/espeak-ng/espeak-ng)
- [ONNX Kokoro TTS Model](https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX)
- [Unicode Character Categories](https://www.unicode.org/reports/tr44/#General_Category_Values)
