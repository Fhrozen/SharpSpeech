<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import type { TtsModel, SynthesisRequest, SynthesisMetrics, TextPreset, StatusType, AsrModel, TranscriptionMetrics, ServerInfo } from './types'
import { presetTexts } from './preset-texts'

import AppHeader from './components/AppHeader.vue'
import ModelSelector from './components/ModelSelector.vue'
import SpeakerSelector from './components/SpeakerSelector.vue'
import LanguageSelector from './components/LanguageSelector.vue'
import TextInput from './components/TextInput.vue'
import ParameterControls from './components/ParameterControls.vue'
import AudioPlayer from './components/AudioPlayer.vue'
import AudioFileInput from './components/AudioFileInput.vue'
import TranscriptionResult from './components/TranscriptionResult.vue'
import StatusMessage from './components/StatusMessage.vue'

const serverInfo = ref<ServerInfo>({ ttsEnabled: false, asrEnabled: false })
const serverInfoLoaded = ref(false)
const activeTab = ref<'tts' | 'asr'>('tts')

const headerSubtitle = computed(() => {
  if (serverInfo.value.ttsEnabled && serverInfo.value.asrEnabled) {
    return 'Multi-Model Text-to-Speech & Speech-to-Text System'
  }
  if (serverInfo.value.asrEnabled) {
    return 'Multi-Model Speech-to-Text System'
  }
  return 'Multi-Model Text-to-Speech System'
})

const models = ref<TtsModel[]>([])
const audioUrl = ref<string | null>(null)
const error = ref('')
const loading = ref(false)
const statusMessage = ref('')
const statusTitle = ref('')
const statusClass = ref<StatusType>('')

const metrics = ref<SynthesisMetrics>({
  processingTime: '0.00s',
  charsPerSecond: '0',
  rtf: '0.000x',
  audioDuration: '0.00s',
  characterCount: '0'
})

const form = reactive({
  model: 'kokoro-q4',
  speaker: '',
  language: '',
  speed: 1.0,
  input: '',
  quality: 8
})

// Language-aware presets that update when form.language changes
const presets = computed<TextPreset[]>(() => {
  // Default to English if language not set or not supported in presets
  const lang = form.language && presetTexts.quote[form.language] ? form.language : 'en'
  
  return [
    {
      id: 'quote',
      label: 'Quote',
      text: presetTexts.quote[lang]
    },
    {
      id: 'paragraph',
      label: 'Paragraph',
      text: presetTexts.paragraph[lang]
    },
    {
      id: 'script',
      label: 'Script',
      text: presetTexts.script[lang]
    }
  ]
})

const speedRange = { min: 0.5, max: 2.0 }
const minCharCount = 10

const selectedModel = computed(() => 
  models.value.find(m => m.name === form.model)
)

const modelSupportsQuality = computed(() => {
  const model = selectedModel.value
  if (!model) return false
  return model.name.includes('supertonic') || model.name.includes('parler')
})

const canGenerate = computed(() => 
  form.input.length >= minCharCount && !loading.value
)

function updateStatus(type: StatusType, title: string, message: string) {
  statusClass.value = type
  statusTitle.value = title
  statusMessage.value = message
}

function syncModelDefaults() {
  const model = selectedModel.value
  if (!model) return

  if (model.speakers.length > 0 && !model.speakers.includes(form.speaker)) {
    form.speaker = model.speakers[0]
  }

  if (model.supportedLanguages.length > 0 && !model.supportedLanguages.includes(form.language)) {
    form.language = model.supportedLanguages[0]
  }
}

async function loadModels() {
  try {
    const response = await fetch('/api/models')
    if (!response.ok) {
      throw new Error('Failed to fetch models')
    }

    const data = await response.json()
    models.value = data

    if (data.length > 0) {
      form.model = data[0].name
      syncModelDefaults()
    }
  } catch (err) {
    updateStatus('error', 'Error', 'Failed to load models')
    error.value = (err as Error).message
  }
}

async function synthesize() {
  loading.value = true
  error.value = ''
  audioUrl.value = null
  
  updateStatus('loading', 'Generating', 'Synthesizing speech...')

  try {
    const payload: SynthesisRequest = {
      model: form.model,
      input: form.input,
      speed: form.speed,
      speaker: form.speaker,
      language: form.language,
      response_format: 'wav',
      voice: form.speaker || 'default'
    }
    
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
      const errorData = await response.json()
      throw new Error(errorData.message || 'Request failed')
    }

    // Extract metrics from response headers
    metrics.value = {
      processingTime: `${response.headers.get('X-Processing-Time') || '0.00'}s`,
      charsPerSecond: response.headers.get('X-Chars-Per-Second') || '0',
      rtf: `${response.headers.get('X-RTF') || '0.000'}x`,
      audioDuration: `${response.headers.get('X-Audio-Duration') || '0.00'}s`,
      characterCount: response.headers.get('X-Character-Count') || '0'
    }

    const blob = await response.blob()
    audioUrl.value = URL.createObjectURL(blob)
    
    updateStatus('success', 'Complete', 'Speech generated successfully!')
    setTimeout(() => {
      statusMessage.value = ''
    }, 3000)
  } catch (err) {
    error.value = (err as Error).message
    updateStatus('error', 'Error', (err as Error).message)
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

// --- ASR (Speech to Text) ---

const asrModels = ref<AsrModel[]>([])
const asrForm = reactive({
  model: '',
  language: '',
  enableVad: false,
  file: null as File | null
})
const transcriptionText = ref<string | null>(null)
const asrLoading = ref(false)
const asrError = ref('')
const asrStatusMessage = ref('')
const asrStatusTitle = ref('')
const asrStatusClass = ref<StatusType>('')

const transcriptionMetrics = ref<TranscriptionMetrics>({
  processingTime: '0.00s',
  rtf: '0.000x',
  audioDuration: '0.00s',
  characterCount: '0'
})

const selectedAsrModel = computed(() =>
  asrModels.value.find(m => m.name === asrForm.model)
)

const canTranscribe = computed(() =>
  asrForm.file !== null && !asrLoading.value
)

function updateAsrStatus(type: StatusType, title: string, message: string) {
  asrStatusClass.value = type
  asrStatusTitle.value = title
  asrStatusMessage.value = message
}

const asrLanguageOptions = computed(() => {
  const model = selectedAsrModel.value
  if (!model) return []
  return model.supportsLanguageAutoDetect ? ['auto', ...model.supportedLanguages] : model.supportedLanguages
})

function syncAsrModelDefaults() {
  const model = selectedAsrModel.value
  if (!model) return

  if (!asrLanguageOptions.value.includes(asrForm.language)) {
    asrForm.language = model.supportsLanguageAutoDetect ? 'auto' : ''
  }

  if (!model.supportsVad) {
    asrForm.enableVad = false
  }
}

async function loadServerInfo() {
  try {
    const response = await fetch('/api/server-info')
    if (response.ok) {
      serverInfo.value = await response.json()
    } else {
      // Endpoint unreachable/unrecognized: fall back to TTS-only (the pre-ASR default).
      serverInfo.value = { ttsEnabled: true, asrEnabled: false }
    }
  } catch (err) {
    serverInfo.value = { ttsEnabled: true, asrEnabled: false }
  }

  activeTab.value = serverInfo.value.ttsEnabled ? 'tts' : 'asr'
  serverInfoLoaded.value = true
}

async function loadAsrModels() {
  try {
    const response = await fetch('/api/asr-models')
    if (!response.ok) {
      throw new Error('Failed to fetch ASR models')
    }

    const data = await response.json()
    asrModels.value = data

    if (data.length > 0) {
      asrForm.model = data[0].name
      syncAsrModelDefaults()
    }
  } catch (err) {
    updateAsrStatus('error', 'Error', 'Failed to load ASR models')
    asrError.value = (err as Error).message
  }
}

async function transcribe() {
  if (!asrForm.file) return

  asrLoading.value = true
  asrError.value = ''
  transcriptionText.value = null

  updateAsrStatus('loading', 'Transcribing', 'Transcribing audio...')

  try {
    const payload = new FormData()
    payload.append('file', asrForm.file)
    payload.append('model', asrForm.model)
    // 'auto' is sent through explicitly (not omitted) so the server always knows auto-detect was chosen.
    if (asrForm.language) {
      payload.append('language', asrForm.language)
    }
    if (asrForm.enableVad) {
      payload.append('use_vad', 'true')
    }

    const response = await fetch('/v1/audio/transcriptions', {
      method: 'POST',
      body: payload
    })

    if (!response.ok) {
      const errorData = await response.json()
      throw new Error(errorData.message || 'Request failed')
    }

    transcriptionMetrics.value = {
      processingTime: `${response.headers.get('X-Processing-Time') || '0.00'}s`,
      rtf: `${response.headers.get('X-RTF') || '0.000'}x`,
      audioDuration: `${response.headers.get('X-Audio-Duration') || '0.00'}s`,
      characterCount: response.headers.get('X-Character-Count') || '0'
    }

    const data = await response.json()
    transcriptionText.value = data.text || ''

    updateAsrStatus('success', 'Complete', 'Transcription complete!')
    setTimeout(() => {
      asrStatusMessage.value = ''
    }, 3000)
  } catch (err) {
    asrError.value = (err as Error).message
    updateAsrStatus('error', 'Error', (err as Error).message)
  } finally {
    asrLoading.value = false
  }
}

onMounted(async () => {
  await loadServerInfo()

  if (serverInfo.value.ttsEnabled) {
    await loadModels()
  }

  if (serverInfo.value.asrEnabled) {
    await loadAsrModels()
  }
})
</script>

<template>
  <main class="app">
    <div class="demo-container">
      <div class="demo-content">
        <AppHeader title="FastTTSR" :subtitle="headerSubtitle" />

        <div v-if="!serverInfoLoaded" class="demo-placeholder">
          <div class="demo-placeholder-icon">⚡</div>
          <p>Loading server capabilities...</p>
        </div>

        <div v-else-if="!serverInfo.ttsEnabled && !serverInfo.asrEnabled" class="demo-error">
          The server is not configured to serve TTS or ASR. Check the <code>SERVER_MODE</code>
          environment variable.
        </div>

        <div v-if="serverInfoLoaded && serverInfo.ttsEnabled && serverInfo.asrEnabled" class="tab-switcher">
          <button
            class="tab-btn"
            :class="{ active: activeTab === 'tts' }"
            @click="activeTab = 'tts'"
          >
            Text to Speech
          </button>
          <button
            class="tab-btn"
            :class="{ active: activeTab === 'asr' }"
            @click="activeTab = 'asr'"
          >
            Speech to Text
          </button>
        </div>

        <template v-if="serverInfo.ttsEnabled">
          <div v-show="activeTab === 'tts'" class="tab-panel">
            <div class="demo-controls">
              <ModelSelector v-model="form.model" :models="models" @update:model-value="syncModelDefaults" />

              <SpeakerSelector
                v-if="selectedModel?.speakers && selectedModel.speakers.length > 0"
                v-model="form.speaker"
                :speakers="selectedModel.speakers"
                :speaker-metadata="selectedModel.speakerMetadata"
              />

              <LanguageSelector
                v-if="selectedModel?.supportedLanguages && selectedModel.supportedLanguages.length > 0"
                v-model="form.language"
                :languages="selectedModel.supportedLanguages"
              />

              <TextInput
                v-model="form.input"
                :presets="presets"
                :min-char-count="minCharCount"
              />
            </div>

            <ParameterControls
              v-model:speed="form.speed"
              v-model:quality="form.quality"
              :show-quality="modelSupportsQuality"
              :speed-min="speedRange.min"
              :speed-max="speedRange.max"
              :loading="loading"
              :can-generate="canGenerate"
              @generate="synthesize"
            />

            <div class="demo-results">
              <AudioPlayer
                :audio-url="audioUrl"
                :metrics="metrics"
                :loading="loading"
                @download="downloadAudio"
              />
            </div>

            <StatusMessage
              :message="statusMessage"
              :title="statusTitle"
              :type="statusClass"
            />

            <div v-if="error" class="demo-error">
              {{ error }}
            </div>
          </div>
        </template>

        <template v-if="serverInfo.asrEnabled">
          <div v-show="activeTab === 'asr'" class="tab-panel">
            <div class="demo-controls">
              <ModelSelector v-model="asrForm.model" :models="asrModels" @update:model-value="syncAsrModelDefaults" />

              <LanguageSelector
                v-if="asrLanguageOptions.length > 0"
                v-model="asrForm.language"
                :languages="asrLanguageOptions"
              />

              <label v-if="selectedAsrModel?.supportsVad" class="vad-toggle">
                <input type="checkbox" v-model="asrForm.enableVad" />
                Voice-activity detection (skip silent audio)
              </label>

              <AudioFileInput v-model="asrForm.file" />
            </div>

            <div class="demo-output-section">
              <button
                class="demo-generate-btn"
                :disabled="!canTranscribe"
                @click="transcribe"
              >
                <span class="icon">🎙️</span>
                <span class="text">{{ asrLoading ? 'Transcribing...' : 'Transcribe' }}</span>
              </button>
            </div>

            <div class="demo-results">
              <TranscriptionResult
                :text="transcriptionText"
                :metrics="transcriptionMetrics"
                :loading="asrLoading"
              />
            </div>

            <StatusMessage
              :message="asrStatusMessage"
              :title="asrStatusTitle"
              :type="asrStatusClass"
            />

            <div v-if="asrError" class="demo-error">
              {{ asrError }}
            </div>
          </div>
        </template>
      </div>
    </div>
  </main>
</template>

<style>
* {
  box-sizing: border-box;
}

body {
  margin: 0;
  padding: 0;
  background: #000000;
  color: #ffffff;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Roboto', 'Oxygen',
    'Ubuntu', 'Cantarell', 'Fira Sans', 'Droid Sans', 'Helvetica Neue', sans-serif;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
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

.tab-switcher {
  display: flex;
  gap: 0.5rem;
  border-bottom: 1px solid #333333;
}

.tab-btn {
  background: transparent;
  color: #888888;
  border: none;
  border-bottom: 2px solid transparent;
  padding: 0.75rem 1rem;
  font-size: 0.95rem;
  cursor: pointer;
  transition: color 0.2s, border-color 0.2s;
}

.tab-btn:hover {
  color: #ffffff;
}

.tab-btn.active {
  color: #fbbf24;
  border-bottom-color: #fbbf24;
}

.tab-panel {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.vad-toggle {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: #cccccc;
  font-size: 0.9rem;
  cursor: pointer;
}

.vad-toggle input[type='checkbox'] {
  accent-color: #fbbf24;
  width: 1rem;
  height: 1rem;
  cursor: pointer;
}

.demo-output-section {
  display: flex;
  justify-content: center;
}

.demo-generate-btn {
  background: transparent;
  color: #fbbf24;
  border: 2px solid #fbbf24;
  padding: 0.75rem 1.75rem;
  border-radius: 0.25rem;
  font-size: 1rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  white-space: nowrap;
}

.demo-generate-btn:hover:not(:disabled) {
  background: rgba(251, 191, 36, 0.1);
  transform: translateY(-1px);
}

.demo-generate-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.demo-controls {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 0;
  background: transparent;
  border-radius: 0;
  border: none;
}

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

.demo-error {
  padding: 1rem 1.5rem;
  background: rgba(239, 68, 68, 0.1);
  color: #f87171;
  border: 1px solid rgba(239, 68, 68, 0.2);
  border-radius: 0.25rem;
  font-family: monospace;
}

.demo-placeholder {
  text-align: center;
  color: #555555;
  padding: 3rem;
}

.demo-placeholder-icon {
  font-size: 3rem;
  margin-bottom: 1rem;
}

.demo-placeholder p {
  margin: 0;
  font-size: 1rem;
  font-weight: 300;
}

@media (max-width: 768px) {
  .demo-container {
    padding: 1rem;
  }
  
  .demo-content {
    gap: 1rem;
  }
}
</style>
