<template>
  <main class="app">
    <div class="demo-container">
      <div class="demo-content">
        <!-- Header -->
        <div class="demo-header-wrapper">
          <span class="demo-header-icon">⚡</span>
          <h2 class="demo-header">
            <span class="demo-header-text">
              <span class="demo-header-bold">FastTTSR</span> | Multi-Model Text-to-Speech System
            </span>
          </h2>
        </div>

        <!-- Controls -->
        <div class="demo-controls">
          <!-- Model Selector -->
          <div class="demo-param">
            <label class="speaker-label">Model: </label>
            <select v-model="form.model" @change="syncModelDefaults" class="model-select">
              <option v-for="model in models" :key="model.name" :value="model.name">
                {{ model.displayName }}
              </option>
            </select>
          </div>

          <!-- Speaker Selection -->
          <div class="demo-param">
            <div class="speaker-container">
              <label class="speaker-label">Speaker: </label>
              <div class="speaker-list">
                <template v-for="(speaker, idx) in selectedModel?.speakers ?? []" :key="speaker">
                  <span 
                    class="speaker-item" 
                    :class="{ active: form.speaker === speaker }"
                    @click="form.speaker = speaker"
                  >
                    {{ formatSpeakerName(speaker) }}
                  </span>
                  <span v-if="idx < (selectedModel?.speakers.length ?? 0) - 1" class="speaker-separator">, </span>
                </template>
              </div>
            </div>
          </div>

          <!-- Language Selection -->
          <div class="demo-param">
            <div class="language-info">
              <label class="speaker-label">Language: </label>
              <div class="speaker-list">
                <template v-for="(lang, idx) in selectedModel?.supportedLanguages ?? []" :key="lang">
                  <span 
                    class="speaker-item" 
                    :class="{ active: form.language === lang }"
                    @click="form.language = lang"
                  >
                    {{ formatLanguageName(lang) }}
                  </span>
                  <span v-if="idx < (selectedModel?.supportedLanguages.length ?? 0) - 1" class="speaker-separator">, </span>
                </template>
              </div>
            </div>
          </div>
        </div>

        <!-- Text Input Section -->
        <div class="demo-input-section">
          <div class="demo-input-label">
            <label>Enter text to synthesize:</label>
          </div>
          <div
            ref="textInput"
            contenteditable="true"
            spellcheck="false"
            class="demo-text-input-editable"
            :class="{ empty: !form.input }"
            :data-placeholder="placeholder"
            @input="handleTextInput"
            @blur="handleBlur"
          ></div>
          
          <!-- Preset Controls -->
          <div class="preset-controls-row">
            <div class="preset-button-group">
              <span class="preset-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="18" height="18">
                  <rect x="4" y="6" width="14" height="2" fill="currentColor"/>
                  <rect x="4" y="10" width="10" height="2" fill="currentColor"/>
                  <rect x="4" y="14" width="14" height="2" fill="currentColor"/>
                  <rect x="4" y="18" width="10" height="2" fill="currentColor"/>
                </svg>
              </span>
              <span 
                v-for="preset in presets" 
                :key="preset.id"
                class="preset-item" 
                :class="{ active: currentPreset === preset.id }"
                @click="applyPreset(preset.id)"
              >
                {{ preset.label }}
              </span>
            </div>
            <span class="demo-char-counter">
              <span 
                v-if="charCount < minCharCount" 
                class="demo-char-warning"
              >
                ⚠️ Minimum {{ minCharCount }} characters required
              </span>
              <span :class="{ warning: charCount < minCharCount }">{{ charCount }}</span> characters
            </span>
          </div>
        </div>

        <!-- Output Section -->
        <div class="demo-output-section">
          <!-- Parameters Row -->
          <div class="demo-params-row">
            <!-- Quality Slider (only for models that support it) -->
            <div v-if="modelSupportsQuality" class="demo-param">
              <div class="demo-param-header">
                <label>
                  Quality: <span class="param-value">{{ form.quality }} Steps</span>
                </label>
              </div>
              <input 
                type="range" 
                v-model.number="form.quality" 
                min="2" 
                max="16" 
                step="1"
                class="demo-slider"
              />
            </div>

            <!-- Speed Slider -->
            <div class="demo-param">
              <div class="demo-param-header">
                <label>
                  Speech Speed: <span class="param-value">{{ form.speed.toFixed(1) }}x</span>
                </label>
              </div>
              <input 
                type="range" 
                v-model.number="form.speed" 
                :min="speedRange.min" 
                :max="speedRange.max" 
                step="0.05"
                class="demo-slider"
              />
            </div>

            <!-- Generate Button -->
            <button 
              class="demo-generate-btn" 
              :disabled="!canGenerate"
              @click="synthesize"
            >
              <span class="icon">⚡</span>
              <span class="text">{{ loading ? 'Generating...' : 'Generate Speech' }}</span>
              <span class="shimmer" aria-hidden="true"></span>
            </button>
          </div>

          <!-- Results Area -->
          <div class="demo-results">
            <div v-if="!audioUrl && !loading" class="demo-placeholder">
              <div class="demo-placeholder-icon">🎙️</div>
              <p>Your generated speech will appear here</p>
            </div>
            
            <!-- Audio Player Result -->
            <div v-if="audioUrl" class="audio-result-container">
              <div class="audio-result-header">
                <div class="audio-result-model">
                  <span class="model-name">{{ selectedModel?.displayName || 'Unknown' }}</span>
                  <span class="model-badge">On-Device</span>
                </div>
                <div class="audio-result-metrics">
                  <div class="metric">
                    <span class="metric-value">{{ generationTime }}</span>
                    <span class="metric-label">Processing Time ↓</span>
                  </div>
                  <div class="metric">
                    <span class="metric-value">{{ charsPerSecond }}</span>
                    <span class="metric-label">Chars/sec ↑</span>
                  </div>
                  <div class="metric">
                    <span class="metric-value">{{ rtf }}</span>
                    <span class="metric-label">RTF ↓</span>
                  </div>
                </div>
              </div>
              
              <div class="audio-player-wrapper">
                <!-- Waveform animation -->
                <div class="waveform-animation" :class="{ playing: isPlaying }">
                  <span></span><span></span><span></span><span></span><span></span>
                </div>
                
                <audio 
                  ref="audioElement"
                  :src="audioUrl" 
                  @play="isPlaying = true"
                  @pause="isPlaying = false"
                  @ended="isPlaying = false"
                  class="audio-player"
                  controls
                ></audio>
                
                <button @click="downloadAudio" class="download-btn" title="Download audio file">
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                    <polyline points="7 10 12 15 17 10"></polyline>
                    <line x1="12" y1="15" x2="12" y2="3"></line>
                  </svg>
                </button>
              </div>
            </div>
          </div>
        </div>

        <!-- Status Box -->
        <div v-if="statusMessage" class="demo-status-box" :class="statusClass">
          <div class="demo-status-content">
            <div class="demo-status-text">
              <strong>{{ statusTitle }}</strong> {{ statusMessage }}
            </div>
          </div>
        </div>

        <!-- Error Display -->
        <div v-if="error" class="demo-error">
          {{ error }}
        </div>
      </div>
    </div>
  </main>
</template>

<script setup>
import { computed, onMounted, reactive, ref, nextTick } from 'vue'

const models = ref([])
const audioUrl = ref('')
const error = ref('')
const loading = ref(false)
const statusMessage = ref('')
const statusTitle = ref('')
const statusClass = ref('')
const textInput = ref(null)
const audioElement = ref(null)
const charCount = ref(0)
const minCharCount = 10
const currentPreset = ref('freeform')
const isPlaying = ref(false)
const generationTime = ref('0.00s')
const charsPerSecond = ref('0')
const rtf = ref('0.000x')
const startTime = ref(0)

const form = reactive({
  model: 'kokoro-q4',
  speaker: '',
  language: '',
  speed: 1.0,
  quality: 8,
  input: ''
})

const placeholder = 'Type any text here to convert it into speech... (minimum 10 characters)'

// Preset templates
const presets = [
  { 
    id: 'freeform', 
    label: 'Freeform',
    text: 'This text-to-speech system runs entirely in your browser, providing fast and private operation without sending any data to external servers.'
  },
  { 
    id: 'quote', 
    label: 'Quote',
    text: 'The only way to do great work is to love what you do. If you haven\'t found it yet, keep looking. Don\'t settle.'
  },
  { 
    id: 'paragraph', 
    label: 'Paragraph',
    text: 'Text-to-speech technology has evolved significantly in recent years. Modern neural networks can now generate highly natural and expressive speech that closely mimics human voices. This advancement opens up numerous possibilities for accessibility, content creation, and human-computer interaction.'
  },
  { 
    id: 'script', 
    label: 'Script',
    text: 'Welcome to our presentation today. We\'ll be exploring the latest developments in artificial intelligence and machine learning. First, let\'s discuss the fundamentals. Then, we\'ll move on to practical applications. Finally, we\'ll look at future trends.'
  }
]

const selectedModel = computed(() => models.value.find((m) => m.name === form.model))

const modelSupportsQuality = computed(() => {
  // Kokoro models don't support quality/steps parameter
  // This would be true for Supertonic-3 or other diffusion-based models
  const model = selectedModel.value
  if (!model) return false
  // Check if model definition indicates quality support
  // For now, we'll use engine type or name as indicator
  return model.engine === 'supertonic' || model.name.includes('supertonic')
})

const speedRange = computed(() => {
  // Different models may have different speed ranges
  if (modelSupportsQuality.value) {
    // Supertonic-3 uses 0.8-1.3 range
    return { min: 0.8, max: 1.3 }
  }
  // Kokoro uses wider range
  return { min: 0.5, max: 2.0 }
})

const canGenerate = computed(() => {
  return !loading.value && charCount.value >= minCharCount && form.speaker && form.language
})

function formatSpeakerName(speaker) {
  // Convert speaker codes like "af_bella" to "Bella"
  if (!speaker) return ''
  const parts = speaker.split('_')
  const name = parts[parts.length - 1]
  return name.charAt(0).toUpperCase() + name.slice(1)
}

function formatLanguageName(lang) {
  // Convert language codes to display names
  const langMap = {
    'en-us': 'English',
    'ja-jp': 'Japanese',
    'es': 'Spanish',
    'fr': 'French',
    'de': 'German',
    'it': 'Italian',
    'pt': 'Portuguese',
    'ru': 'Russian',
    'zh': 'Chinese',
    'ko': 'Korean',
    'ar': 'Arabic',
    'hi': 'Hindi',
    'nl': 'Dutch',
    'pl': 'Polish',
    'tr': 'Turkish',
    'sv': 'Swedish',
    'da': 'Danish',
    'fi': 'Finnish',
    'no': 'Norwegian',
    'cs': 'Czech',
    'el': 'Greek',
    'hu': 'Hungarian',
    'ro': 'Romanian',
    'th': 'Thai',
    'vi': 'Vietnamese',
    'id': 'Indonesian',
    'uk': 'Ukrainian',
    'bg': 'Bulgarian',
    'hr': 'Croatian',
    'sk': 'Slovak',
    'sl': 'Slovenian',
    'et': 'Estonian',
    'lv': 'Latvian',
    'lt': 'Lithuanian'
  }
  return langMap[lang.toLowerCase()] || lang.toUpperCase()
}

function handleTextInput(e) {
  form.input = e.target.innerText
  charCount.value = form.input.length
}

function handleBlur() {
  // Ensure we capture the final text
  if (textInput.value) {
    form.input = textInput.value.innerText
  }
}

function applyPreset(presetId) {
  currentPreset.value = presetId
  const preset = presets.find(p => p.id === presetId)
  if (preset && textInput.value) {
    textInput.value.innerText = preset.text
    form.input = preset.text
    charCount.value = preset.text.length
  }
}

function syncModelDefaults() {
  const current = selectedModel.value
  if (!current) return
  form.speaker = current.speakers[0] ?? ''
  form.language = current.supportedLanguages[0] ?? ''
  
  // Reset speed to default for model
  form.speed = 1.0
  
  // Show status message
  updateStatus('info', 'Model Changed', `Switched to ${current.displayName}`)
}

function updateStatus(type, title, message) {
  statusTitle.value = title
  statusMessage.value = message
  statusClass.value = `status-${type}`
  
  // Auto-clear after 3 seconds for info messages
  if (type === 'info') {
    setTimeout(() => {
      statusMessage.value = ''
    }, 3000)
  }
}

async function loadModels() {
  try {
    updateStatus('loading', 'Loading', 'Fetching available models...')
    const response = await fetch('/api/models')
    models.value = await response.json()
    
    if (!models.value.length) {
      updateStatus('error', 'Error', 'No models available')
      return
    }
    
    if (!models.value.some((m) => m.name === form.model)) {
      form.model = models.value[0].name
    }
    
    syncModelDefaults()
    updateStatus('success', 'Ready', `${models.value.length} model(s) loaded successfully`)
    
    // Auto-clear success message
    setTimeout(() => {
      statusMessage.value = ''
    }, 2000)
  } catch (err) {
    updateStatus('error', 'Error', 'Failed to load models')
    error.value = err.message
  }
}

async function synthesize() {
  loading.value = true
  error.value = ''
  audioUrl.value = ''
  startTime.value = Date.now()
  
  updateStatus('loading', 'Generating', 'Synthesizing speech...')

  try {
    const payload = {
      model: form.model,
      input: form.input,
      speed: form.speed,
      speaker: form.speaker,
      language: form.language,
      response_format: 'wav',
      voice: form.speaker || 'default'
    }
    
    // Only include quality for models that support it
    if (modelSupportsQuality.value) {
      payload.quality = form.quality
    }

    const response = await fetch('/v1/audio/speech', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(payload)
    })

    if (!response.ok) {
      const payload = await response.json()
      throw new Error(payload.message || 'Request failed')
    }

    const blob = await response.blob()
    audioUrl.value = URL.createObjectURL(blob)
    
    // Calculate metrics
    const endTime = Date.now()
    const duration = (endTime - startTime.value) / 1000
    const chars = form.input.length
    
    generationTime.value = `${duration.toFixed(2)}s`
    charsPerSecond.value = `${(chars / duration).toFixed(1)}`
    
    // Get audio duration for RTF calculation
    const audio = new Audio(audioUrl.value)
    audio.addEventListener('loadedmetadata', () => {
      const audioDuration = audio.duration
      rtf.value = `${(duration / audioDuration).toFixed(3)}x`
    })
    
    updateStatus('success', 'Complete', 'Speech generated successfully!')
    setTimeout(() => {
      statusMessage.value = ''
    }, 3000)
  } catch (err) {
    error.value = err.message
    updateStatus('error', 'Error', err.message)
  } finally {
    loading.value = false
  }
}

function downloadAudio() {
  if (!audioUrl.value) return
  
  const a = document.createElement('a')
  a.href = audioUrl.value
  a.download = `fastttsr-${form.model}-${Date.now()}.wav`
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
}

onMounted(async () => {
  await loadModels()
  
  // Initialize text input with first preset
  await nextTick()
  if (textInput.value) {
    textInput.value.innerText = presets[0].text
    form.input = presets[0].text
    charCount.value = presets[0].text.length
  }
})
</script>

<style>
* {
  box-sizing: border-box;
}

body {
  margin: 0;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Roboto', 'Helvetica', 'Arial', sans-serif;
  background: #000000;
  color: #ffffff;
  line-height: 1.5;
}

.app {
  min-height: 100vh;
  padding: 0;
  background: #000000;
}

.demo-container {
  max-width: 1200px;
  margin: 0 auto;
  background: transparent;
  border-radius: 0;
  padding: 1.5rem 2rem 2rem;
  box-shadow: none;
  border: none;
}

.demo-content {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

/* Header */
.demo-header-wrapper {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.demo-header-icon {
  font-size: 2.5rem;
  filter: none;
}

.demo-header {
  margin: 0;
  font-size: 1.75rem;
  font-weight: 400;
  color: #ffffff;
  letter-spacing: -0.01em;
}

.demo-header-text {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.demo-header-bold {
  font-weight: 700;
  background: none;
  -webkit-background-clip: unset;
  -webkit-text-fill-color: unset;
  background-clip: unset;
  color: #ffffff;
}

/* Controls */
.demo-controls {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 0;
  background: transparent;
  border-radius: 0;
  border: none;
}

.demo-param {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.speaker-label {
  font-weight: 400;
  color: #ffffff;
  font-size: 1rem;
  margin: 0;
}

.model-select {
  padding: 0.65rem 1rem;
  background: #1a1a1a;
  border: 1px solid #333333;
  border-radius: 0.25rem;
  color: #ffffff;
  font-size: 1rem;
  cursor: pointer;
  transition: border-color 0.2s;
  max-width: 300px;
}

.model-select:hover {
  border-color: #666666;
  background: #1a1a1a;
}

.model-select:focus {
  outline: none;
  border-color: #666666;
  box-shadow: none;
}

.speaker-container,
.language-info {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0;
}

.speaker-list {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0;
  margin-left: 0.5rem;
}

.speaker-item {
  cursor: pointer;
  color: #888888;
  transition: color 0.2s;
  padding: 0;
  border-radius: 0;
  font-size: 1rem;
  background: transparent;
}

.speaker-item:hover {
  color: #ffffff;
  background: transparent;
}

.speaker-item.active {
  color: #ffffff;
  font-weight: 400;
  background: transparent;
}

.speaker-separator {
  color: #888888;
  margin: 0 0.15rem;
}

/* Text Input Section */
.demo-input-section {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  margin-top: 1.5rem;
}

.demo-input-label label {
  font-weight: 400;
  color: #ffffff;
  font-size: 1rem;
  display: none;
}

.demo-text-input-editable {
  min-height: 180px;
  padding: 2rem 2rem 2rem 2.5rem;
  background: transparent;
  border: none;
  border-left: 4px solid #ffffff;
  border-radius: 0;
  color: #ffffff;
  font-size: 1.5rem;
  line-height: 1.6;
  transition: border-color 0.2s;
  outline: none;
  overflow-y: auto;
  max-height: 400px;
  font-weight: 300;
}

.demo-text-input-editable:focus {
  border-left-color: #fbbf24;
  box-shadow: none;
}

.demo-text-input-editable.empty:before {
  content: attr(data-placeholder);
  color: #555555;
  pointer-events: none;
  position: absolute;
}

/* Preset Controls */
.preset-controls-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 1rem;
  padding-top: 0;
}

.preset-button-group {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  flex-wrap: wrap;
}

.preset-icon {
  display: flex;
  align-items: center;
  color: #666666;
  margin-right: 0.5rem;
}

.preset-item {
  cursor: pointer;
  padding: 0.5rem 1rem;
  background: transparent;
  border: 1px solid #333333;
  border-radius: 0.25rem;
  color: #888888;
  font-size: 0.9rem;
  transition: all 0.2s;
  font-weight: 400;
}

.preset-item:hover {
  background: transparent;
  border-color: #666666;
  color: #ffffff;
}

.preset-item.active {
  background: transparent;
  border-color: #ffffff;
  color: #ffffff;
}

.demo-char-counter {
  font-size: 0.9rem;
  color: #666666;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.demo-char-counter .warning {
  color: #fbbf24;
  font-weight: 400;
}

.demo-char-warning {
  color: #ef4444;
  font-weight: 400;
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

/* Output Section */
.demo-output-section {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  margin-top: 1rem;
}

.demo-params-row {
  display: grid;
  grid-template-columns: 1fr 1fr auto;
  gap: 1.5rem;
  align-items: end;
}

.demo-param-header {
  margin-bottom: 0.75rem;
}

.demo-param-header label {
  font-size: 0.95rem;
  color: #ffffff;
  font-weight: 400;
}

.param-value {
  color: #fbbf24;
  font-weight: 400;
}

.demo-slider {
  width: 100%;
  height: 4px;
  border-radius: 2px;
  background: #333333;
  outline: none;
  -webkit-appearance: none;
  border: none;
}

.demo-slider::-webkit-slider-thumb {
  -webkit-appearance: none;
  appearance: none;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  background: #ffffff;
  cursor: pointer;
  box-shadow: none;
  transition: background 0.2s;
}

.demo-slider::-webkit-slider-thumb:hover {
  background: #fbbf24;
  transform: none;
}

.demo-slider::-moz-range-thumb {
  width: 16px;
  height: 16px;
  border-radius: 50%;
  background: #ffffff;
  cursor: pointer;
  border: none;
  box-shadow: none;
  transition: background 0.2s;
}

.demo-slider::-moz-range-thumb:hover {
  background: #fbbf24;
  transform: none;
}

/* Generate Button */
.demo-generate-btn {
  position: relative;
  padding: 1rem 2.5rem;
  background: transparent;
  border: 2px solid #fbbf24;
  border-radius: 2rem;
  color: #ffffff;
  font-weight: 400;
  font-size: 1rem;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  transition: all 0.2s;
  overflow: hidden;
  white-space: nowrap;
}

.demo-generate-btn:hover:not(:disabled) {
  transform: none;
  box-shadow: none;
  background: rgba(251, 191, 36, 0.1);
}

.demo-generate-btn:disabled {
  opacity: 0.3;
  cursor: not-allowed;
}

.demo-generate-btn .icon {
  font-size: 1.25rem;
  filter: none;
}

.demo-generate-btn .shimmer {
  display: none;
}

/* Results */
.demo-results {
  min-height: 120px;
  padding: 1.5rem;
  background: transparent;
  border: 1px solid #333333;
  border-radius: 0.25rem;
  display: flex;
  align-items: center;
  justify-content: center;
}

.demo-placeholder {
  text-align: center;
  color: #555555;
}

.demo-placeholder-icon {
  font-size: 3rem;
  margin-bottom: 1rem;
  opacity: 0.3;
}

.demo-placeholder p {
  margin: 0;
  font-size: 1rem;
  font-weight: 300;
}

/* Audio Result Container */
.audio-result-container {
  width: 100%;
  background: #1a1a1a;
  border-radius: 0.25rem;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.audio-result-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
}

.audio-result-model {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.model-name {
  color: #ffffff;
  font-size: 0.875rem;
  font-weight: 500;
}

.model-badge {
  background: #333333;
  color: #888888;
  font-size: 0.75rem;
  padding: 0.125rem 0.5rem;
  border-radius: 0.25rem;
  font-weight: 500;
}

.audio-result-metrics {
  display: flex;
  gap: 1.5rem;
}

.metric {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.125rem;
}

.metric-value {
  color: #fbbf24;
  font-size: 0.875rem;
  font-weight: 600;
  font-family: monospace;
}

.metric-label {
  color: #888888;
  font-size: 0.75rem;
  font-weight: 400;
}

.audio-player-wrapper {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.waveform-animation {
  display: flex;
  align-items: center;
  gap: 0.125rem;
  height: 24px;
  flex-shrink: 0;
}

.waveform-animation span {
  width: 2px;
  height: 8px;
  background: #888888;
  border-radius: 1px;
  transition: all 0.3s ease;
}

.waveform-animation.playing span {
  background: #fbbf24;
  animation: wave 1s ease-in-out infinite;
}

.waveform-animation.playing span:nth-child(1) { animation-delay: 0s; }
.waveform-animation.playing span:nth-child(2) { animation-delay: 0.1s; }
.waveform-animation.playing span:nth-child(3) { animation-delay: 0.2s; }
.waveform-animation.playing span:nth-child(4) { animation-delay: 0.3s; }
.waveform-animation.playing span:nth-child(5) { animation-delay: 0.4s; }

@keyframes wave {
  0%, 100% { height: 8px; }
  50% { height: 20px; }
}

.audio-player {
  flex: 1;
  outline: none;
}

.download-btn {
  background: transparent;
  border: 1px solid #333333;
  color: #888888;
  width: 32px;
  height: 32px;
  border-radius: 0.25rem;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: all 0.2s;
  flex-shrink: 0;
}

.download-btn:hover {
  border-color: #fbbf24;
  color: #fbbf24;
}

/* Status Box */
.demo-status-box {
  padding: 1rem 1.5rem;
  border-radius: 0.25rem;
  border: none;
  animation: fadeIn 0.3s;
  font-family: monospace;
}

@keyframes fadeIn {
  from {
    opacity: 0;
    transform: translateY(-10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.status-loading {
  background: #4a3a00;
  border-color: transparent;
  color: #fbbf24;
}

.status-success {
  background: #1a4d2e;
  border-color: transparent;
  color: #86efac;
}

.status-error {
  background: #4d1a1a;
  border-color: transparent;
  color: #fca5a5;
}

.status-info {
  background: #1a2a4d;
  border-color: transparent;
  color: #93c5fd;
}

.demo-status-text {
  font-size: 0.9rem;
}

.demo-status-text strong {
  font-weight: 600;
}

/* Error */
.demo-error {
  padding: 1rem 1.5rem;
  background: #4d1a1a;
  border: none;
  border-radius: 0.25rem;
  color: #fca5a5;
  font-size: 0.9rem;
}

/* Responsive */
@media (max-width: 768px) {
  .demo-container {
    padding: 2rem 1rem;
  }

  .demo-header {
    font-size: 1.5rem;
  }

  .demo-text-input-editable {
    font-size: 1.25rem;
    padding: 1.5rem 1.5rem 1.5rem 2rem;
  }

  .demo-params-row {
    grid-template-columns: 1fr;
    gap: 1.5rem;
  }

  .demo-generate-btn {
    width: 100%;
    justify-content: center;
  }

  .preset-controls-row {
    flex-direction: column;
    align-items: stretch;
  }

  .demo-char-counter {
    justify-content: space-between;
  }
}
</style>
