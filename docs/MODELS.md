# TTS & ASR Models Documentation

FastTTSR supports multiple state-of-the-art Text-to-Speech models and Speech-to-Text (ASR) models, each with unique characteristics and use cases.

## Table of Contents

- [Model Overview](#model-overview)
- [Kokoro Models](#kokoro-models)
- [Supertonic Models](#supertonic-models)
- [ASR Models](#asr-models)
- [Voice Catalog](#voice-catalog)
- [Language Support](#language-support)
- [Model Comparison](#model-comparison)
- [Adding Custom Models](#adding-custom-models)

---

## Model Overview

**Text-to-Speech**

| Model | Type | Size | Quality | Speed | Languages | Voices | Best For |
|-------|------|------|---------|-------|-----------|--------|----------|
| kokoro-q4 | Quantized | ~100MB | Good | Fast | 9 | 51+ | Production, low latency |
| kokoro-full | Full precision | ~350MB | Excellent | Medium | 9 | 51+ | High quality output |
| supertonic-3 | Multilingual | ~200MB | Excellent | Medium | 3+ | 10 styles | Multilingual apps |

**Speech-to-Text**

| Model | Type | Size | Languages | Auto-Detect | Best For |
|-------|------|------|-----------|--------------|----------|
| whisper-base | GGML (whisper.cpp) | ~140MB | Multilingual | ✅ | Simple whole-file transcription |
| nemotron-3.5 | ONNX (INT4) | ~600MB | 35+ | ✅ | Streaming/cache-aware decoding, broad language coverage |

---

## Kokoro Models

### Overview

Kokoro-82M is a fast, lightweight TTS model with excellent voice quality. Built on transformer architecture, it uses flow-matching for natural prosody.

**Key Features:**
- 82 million parameters
- Direct ONNX inference
- espeak-ng phonemization
- 24kHz 16-bit PCM output
- 5+ speaker voices
- Sub-second synthesis for short texts

### Kokoro-Q4 (Quantized)

**Model ID:** `kokoro-q4`

**Specifications:**
- **Size:** ~100MB (model) + ~500KB per voice
- **Precision:** INT8 quantized
- **Memory Usage:** 1-2GB runtime
- **Latency:** 50-150ms (typical sentence)
- **Quality:** Good (slight quality loss vs full)

**Use Cases:**
- Production deployments with limited resources
- High-throughput applications
- Cost-sensitive environments
- Mobile/edge deployment

**Download URLs:**
- Model: `https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/onnx/model_q4.onnx`
- Voices: `https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/voices/{voice}.bin`

### Kokoro-Full (Full Precision)

**Model ID:** `kokoro-full`

**Specifications:**
- **Size:** ~350MB (model) + ~500KB per voice
- **Precision:** FP32 (float32)
- **Memory Usage:** 2-4GB runtime
- **Latency:** 80-200ms (typical sentence)
- **Quality:** Excellent (best quality)

**Use Cases:**
- Premium applications
- Audio production
- Content creation
- When quality is priority over speed

**Download URLs:**
- Model: `https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/onnx/model.onnx`
- Voices: `https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/voices/{voice}.bin`

### Technical Details

**Architecture:**
- Transformer-based encoder-decoder
- Flow-matching vocoder
- Speaker conditioning via embeddings
- IPA phoneme input (via espeak-ng)

**Processing Pipeline:**
```
Text → espeak-ng → IPA Phonemes → Token IDs → 
ONNX Model (with speaker embedding) → Mel-spectrogram → 
Vocoder → PCM Audio → WAV File
```

**Input Format:**
- Phonemes in International Phonetic Alphabet (IPA)
- Maximum length: ~500 characters
- Special tokens: `_` (phoneme separator)

**Output Format:**
- Sample Rate: 24000 Hz
- Bit Depth: 16-bit
- Channels: Mono
- Format: PCM WAV

**Speaker Embeddings:**
- Format: Binary float32 array
- Shape: [510, 256]
- Size: ~500KB per voice file
- Naming: `{prefix}_{name}.bin` (e.g., `af_bella.bin`)

---

## Supertonic Models

### Overview

Supertonic-3 is a multilingual, high-quality TTS model with support for 31 languages. It uses a multi-model architecture with separate components for text encoding, duration prediction, and vocoding.

**Key Features:**
- 31 language support
- 10 preset voice styles (5 female, 5 male)
- Flow-matching inference
- Natural prosody and intonation
- 22.05kHz output
- Professional audio quality

### Supertonic-3

**Model ID:** `supertonic-3`

**Specifications:**
- **Size:** ~200MB (4 models combined)
- **Precision:** FP32 (float32)
- **Memory Usage:** 2-3GB runtime
- **Latency:** 100-400ms (typical sentence)
- **Quality:** Excellent (production grade)

**Components:**
1. **Text Encoder** - Converts text to embeddings
2. **Duration Predictor** - Predicts phoneme durations
3. **Vector Estimator** - Estimates style vectors
4. **Vocoder** - Generates audio waveform

**Use Cases:**
- Multilingual applications
- Global products
- High-quality voice synthesis
- When language coverage is important

**Download URLs:**
- Base: `https://huggingface.co/Supertone/supertonic-3/resolve/main/`
- Models: `onnx/text_encoder.onnx`, `onnx/duration_predictor.onnx`, etc.
- Voices: `voice_styles/{F1-F5,M1-M5}.json`

### Technical Details

**Architecture:**
- Multi-stage pipeline
- Flow-matching vocoder
- JSON-based voice styles
- Unicode-based text encoding (no phonemization)

**Processing Pipeline:**
```
Text → Unicode Indexer → Text Encoder → 
Duration Predictor → Vector Estimator → 
Vocoder → PCM Audio → WAV File
```

**Input Format:**
- Direct text (no phonemization)
- Unicode characters
- Language-agnostic encoding

**Output Format:**
- Sample Rate: 22050 Hz
- Bit Depth: 16-bit
- Channels: Mono
- Format: PCM WAV

**Voice Styles:**
- Format: JSON configuration
- Parameters: pitch, energy, speaking rate, style embedding
- Customizable via JSON editing

---

## ASR Models

FastTTSR also supports Speech-to-Text (ASR/transcription) via `/v1/audio/transcriptions`, when
`SERVER_MODE` enables ASR (`asr` or `both`). Models are selected the same way as TTS: by `name` in
the request.

### Overview

| Model | Engine | Languages | Auto-Detect | VAD | Notes |
|-------|--------|-----------|--------------|-----|-------|
| `whisper-base` | Whisper.net (whisper.cpp/GGML) | Multilingual | ✅ | ❌ | Whole-file batch transcription, requires 16kHz mono input (resampled automatically) |
| `nemotron-3.5` | Raw ONNX Runtime (FastConformer-RNNT) | 35+ | ✅ | ✅ | Cache-aware streaming architecture, chunked internally |

### Whisper Base

**Model ID:** `whisper-base`

**Specifications:**
- **Format:** GGML (`ggml-base.bin`), run via [Whisper.net](https://github.com/sandrohanea/whisper.net)
- **Input:** 16kHz mono PCM (non-conforming input is resampled by `WavAudioUtils.ResampleToMono16kWav`)
- **Language:** Auto-detected by default, or hinted via the `language` form field

**Download URL:**
- Model: `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin`

**Processing Pipeline:**
```
Audio Upload → Resample to 16kHz mono → Whisper.net processor →
Segment-by-segment transcription → Concatenated text
```

### Nemotron 3.5 ASR

**Model ID:** `nemotron-3.5`

**Specifications:**
- **Format:** ONNX (INT4 quantized), 3 chained models: `encoder.onnx`, `decoder.onnx`, `joint.onnx`
  (each with an accompanying `.onnx.data` weights file)
- **Architecture:** Cache-aware streaming FastConformer-RNNT — processes audio in fixed-size
  chunks (65 frames: 9 pre-encode cache + 56 new frames), threading `cache_last_channel`/
  `cache_last_time` tensors between chunks so the model's internal state carries over as if
  streaming, even though today's HTTP API is whole-file batch only
- **Languages:** 35+ (see `src/FastTTSR.Api/config.json` for the full list); language conditioning
  uses a fixed `lang_id` integer table (`Services/NemotronLanguages.cs`), not vocab.txt tags
- **Feature extraction:** all hyperparameters (mel scale/filterbank, framing, `log_eps`, chunk
  size, `lang_id` mapping) are sourced from `genai_config.json`, ported from a validated
  Python/onnxruntime reference implementation and confirmed accurate via real-audio circular
  tests (~1-2% WER on multi-sentence paragraphs, near-perfect on a 20-turn conversation)
- **Voice-activity detection (VAD):** optional, opt-in via the `use_vad` form field
  (`supportsVad: true`). Uses the bundled `silero_vad.onnx` asset (`Services/SileroVadEngine.cs` +
  `Services/SileroVadGate.cs`, ported from the Python reference's `SileroVadOrt`/`VadGate`) to
  skip encoder/decoder inference on chunks classified as silence after enough consecutive silent
  chunks accumulate (thresholds from `genai_config.json`'s `vad` section) - reduces compute on
  audio with long silences without changing the transcript for chunks that do contain speech.

**Download URL (source):**
- `https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4`

**Processing Pipeline:**
```
Audio Upload → Feature extraction (log-mel) → Encoder (chunked, cache-threaded) →
LSTM decoder/predictor → Joint network → Greedy RNNT decoding → Vocabulary lookup → Text
```

---

## Voice Catalog

### Kokoro Voices

**510+ speakers** organized by prefix:

#### Prefixes

| Prefix | Description | Count |
|--------|-------------|-------|
| `af` | American Female | ~100 |
| `am` | American Male | ~100 |
| `bf` | British Female | ~100 |
| `bm` | British Male | ~100 |
| Others | Various accents/styles | ~110 |

#### Notable Voices

**American Female (`af`):**
- `af_alloy` - Balanced, neutral (OpenAI alias: `alloy`)
- `af_bella` - Warm, friendly
- `af_nicole` - Professional, clear
- `af_sarah` - Soft, calm
- `af_sky` - Bright, energetic
- `af_nova` - Expressive (OpenAI alias: `nova`)
- `af_shimmer` - Gentle (OpenAI alias: `shimmer`)

**American Male (`am`):**
- `am_adam` - Deep, authoritative
- `am_michael` - Conversational
- `am_echo` - Resonant (OpenAI alias: `echo`)

**British Female (`bf`):**
- `bf_emma` - Received Pronunciation
- `bf_isabella` - Refined
- `bf_fable` - Storytelling (OpenAI alias: `fable`)

**British Male (`bm`):**
- `bm_george` - Distinguished
- `bm_lewis` - Casual
- `bm_onyx` - Strong (OpenAI alias: `onyx`)

#### Voice Selection Tips

1. **For clarity:** `af_nicole`, `am_michael`
2. **For warmth:** `af_bella`, `am_adam`
3. **For energy:** `af_sky`, `am_echo`
4. **For professional:** `bf_emma`, `bm_george`
5. **For narration:** `bf_fable`, `bm_onyx`

### Supertonic-3 Voices

**10 preset voice styles:**

#### Female Voices

| ID | Name | Description | Best For |
|----|------|-------------|----------|
| `F1` | Female 1 | Warm, friendly | Customer service, tutorials |
| `F2` | Female 2 | Professional, clear | Business, presentations |
| `F3` | Female 3 | Energetic, youthful | Marketing, advertisements |
| `F4` | Female 4 | Calm, soothing | Meditation, audiobooks |
| `F5` | Female 5 | Authoritative, confident | News, announcements |

#### Male Voices

| ID | Name | Description | Best For |
|----|------|-------------|----------|
| `M1` | Male 1 | Deep, resonant | Narration, documentaries |
| `M2` | Male 2 | Conversational, casual | Podcasts, vlogs |
| `M3` | Male 3 | Professional, formal | Corporate, training |
| `M4` | Male 4 | Friendly, approachable | Customer service, guides |
| `M5` | Male 5 | Strong, commanding | Announcements, alerts |

---

## Language Support

### Kokoro Languages

**9 languages supported:**

| Language | Code | Aliases | Quality | Notes |
|----------|------|---------|---------|-------|
| English (US) | `en-us` | `en`, `a`, `english` | Excellent | Default |
| English (GB) | `en-gb` | `b` | Excellent | British accent |
| Spanish | `es` | `d`, `spanish` | Very Good | Neutral accent |
| French | `fr-fr` | `e`, `f`, `fr`, `french` | Very Good | Standard French |
| Hindi | `hi` | `g`, `hindi` | Good | Indian Hindi |
| Italian | `it` | `h`, `italian` | Very Good | Standard Italian |
| Japanese | `ja-jp` | `j`, `ja`, `japanese` | Excellent | Tokyo dialect |
| Portuguese (BR) | `pt-br` | `p`, `pt`, `portuguese` | Very Good | Brazilian Portuguese |
| Chinese (CN) | `zh-cn` | `z`, `zh`, `chinese` | Very Good | Mandarin |

**Example Usage:**
```json
{
  "model": "kokoro-q4",
  "input": "こんにちは、世界！",
  "voice": "af_bella",
  "language": "ja-jp"
}
```

### Supertonic-3 Languages

**31 languages supported:**

| Language | Code | Quality |
|----------|------|---------|
| English | `en` | Excellent |
| Spanish | `es` | Excellent |
| French | `fr` | Excellent |
| German | `de` | Excellent |
| Italian | `it` | Excellent |
| Portuguese | `pt` | Excellent |
| Polish | `pl` | Very Good |
| Turkish | `tr` | Very Good |
| Russian | `ru` | Very Good |
| Dutch | `nl` | Very Good |
| Czech | `cs` | Very Good |
| Arabic | `ar` | Very Good |
| Chinese | `zh` | Excellent |
| Japanese | `ja` | Excellent |
| Korean | `ko` | Excellent |
| Hungarian | `hu` | Good |
| Hindi | `hi` | Good |
| Swedish | `sv` | Very Good |
| Danish | `da` | Very Good |
| Norwegian | `no` | Very Good |
| Finnish | `fi` | Very Good |
| Greek | `el` | Good |
| Romanian | `ro` | Good |
| Ukrainian | `uk` | Very Good |
| Thai | `th` | Good |
| Vietnamese | `vi` | Good |
| Indonesian | `id` | Good |
| Hebrew | `he` | Good |
| Persian | `fa` | Good |
| Bengali | `bn` | Good |
| Tamil | `ta` | Good |

**Example Usage:**
```json
{
  "model": "supertonic-3",
  "input": "Bonjour le monde!",
  "voice": "F2",
  "language": "fr"
}
```

---

## Model Comparison

### Performance Comparison

**Benchmark Setup:**
- Input: 200 character sentence
- Hardware: 2 CPU cores, 4GB RAM
- Concurrent: 1 request

| Model | Latency | Throughput | Memory | Quality |
|-------|---------|------------|--------|---------|
| kokoro-q4 | 150ms | 6.7 req/sec | 1.5GB | 8/10 |
| kokoro-full | 220ms | 4.5 req/sec | 3.2GB | 9/10 |
| supertonic-3 | 380ms | 2.6 req/sec | 2.8GB | 9/10 |

### Quality Comparison

**Subjective Evaluation (1-10 scale):**

| Criteria | kokoro-q4 | kokoro-full | supertonic-3 |
|----------|-----------|-------------|--------------|
| Naturalness | 8 | 9 | 9 |
| Pronunciation | 8 | 9 | 8 |
| Prosody | 7 | 8 | 9 |
| Emotion | 7 | 8 | 8 |
| Clarity | 9 | 9 | 9 |
| **Overall** | **8** | **9** | **9** |

### Use Case Recommendations

**Choose kokoro-q4 when:**
- ✅ Need low latency (< 200ms)
- ✅ Limited resources (< 2GB RAM)
- ✅ High throughput required
- ✅ Cost is a concern
- ✅ Quality is "good enough"

**Choose kokoro-full when:**
- ✅ Need best quality
- ✅ Have sufficient resources (4GB+ RAM)
- ✅ Production audio content
- ✅ User experience is priority
- ✅ Limited to 9 languages

**Choose supertonic-3 when:**
- ✅ Need 31 language support
- ✅ Multilingual application
- ✅ Excellent quality required
- ✅ Can tolerate higher latency
- ✅ Global audience

---

## Adding Custom Models

### 1. Prepare Model Files

Required files:
- ONNX model file(s)
- Voice files (.bin for Kokoro, .json for others)
- Metadata files (tokens.txt, etc.)

### 2. Update config.json

Add model definition:
```json
{
  "models": [
    {
      "name": "my-custom-model",
      "displayName": "My Custom Model",
      "description": "Custom TTS model description",
      "engine": "kokoro",
      "modelPath": "onnx/my_model.onnx",
      "tokensPath": "/app/assets/tokens.txt",
      "voicesPath": "voices",
      "voicesBaseUrl": "https://example.com/voices",
      "voiceFileExtension": ".bin",
      "assets": [
        {
          "relativePath": "onnx/my_model.onnx",
          "url": "https://example.com/my_model.onnx"
        }
      ],
      "supportedLanguages": ["en-us", "es"],
      "speakers": ["voice1", "voice2"]
    }
  ]
}
```

### 3. Implement Custom Engine (if needed)

If using a new engine type, implement `ITtsSynthesizer`:

```csharp
public class MyCustomSynthesizer : ITtsSynthesizer, IIdleTrackingSynthesizer
{
    public DateTimeOffset LastActivity { get; set; }

    public async Task<SynthesisResult> SynthesizeAsync(
        TtsModelDefinition model,
        string text,
        string? speaker,
        string? language,
        float speed,
        CancellationToken cancellationToken)
    {
        LastActivity = DateTimeOffset.UtcNow;
        
        // Custom synthesis logic
        var audioBytes = await SynthesizeInternalAsync(...);
        
        return new SynthesisResult(audioBytes, success: true);
    }

    public void Dispose()
    {
        // Cleanup resources
    }
}
```

### 4. Register in DI Container

Update `Program.cs`:
```csharp
builder.Services.AddSingleton<MyCustomSynthesizer>();
```

### 5. Update Router

Update `TtsSynthesizerRouter.cs`:
```csharp
if (model.Engine == "my-custom-engine")
{
    return await _myCustomSynthesizer.SynthesizeAsync(...);
}
```

### 6. Test

```bash
curl -X POST http://localhost:5768/v1/audio/speech \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "my-custom-model",
    "input": "Test custom model",
    "voice": "voice1"
  }' \
  --output test.wav
```

---

## Model Optimization Tips

### 1. Memory Optimization

- **Use quantized models** (kokoro-q4) for lower memory
- **Enable idle unloading** to free unused models
- **Limit concurrent models** in memory
- **Pre-load frequently used voices** only

### 2. Performance Optimization

- **Cache audio** for repeated phrases
- **Batch requests** when possible
- **Use CDN** for model downloads
- **Optimize phonemization** (thread-safe locking)

### 3. Quality Optimization

- **Use full-precision models** for best quality
- **Proper text preprocessing** (sanitization)
- **Correct language selection** for pronunciation
- **Appropriate speed settings** (0.8-1.2 range)

---

## Model Licensing

### Kokoro

- **License:** Apache 2.0
- **Commercial Use:** ✅ Allowed
- **Attribution:** Required
- **Source:** https://github.com/hexgrad/Kokoro-82M

### Supertonic-3

- **License:** Custom (check Hugging Face)
- **Commercial Use:** Check terms
- **Attribution:** Required
- **Source:** https://huggingface.co/Supertone/supertonic-3

### Whisper (whisper.cpp / GGML)

- **License:** MIT
- **Commercial Use:** ✅ Allowed
- **Attribution:** Required
- **Source:** https://github.com/ggerganov/whisper.cpp

### Nemotron 3.5 ASR

- **License:** Custom (check Hugging Face / NVIDIA terms)
- **Commercial Use:** Check terms
- **Attribution:** Required
- **Source:** https://huggingface.co/onnx-community/nemotron-3.5-asr-streaming-0.6b-onnx-int4

**Note:** Always verify license terms before production use.

---

## Future Models

Planned model support:
- [ ] StyleTTS 2
- [ ] XTTS v2
- [ ] Bark
- [ ] Tortoise TTS
- [ ] Custom fine-tuned models

**Want to add a model?** Open an issue on GitHub with model details and requirements.
