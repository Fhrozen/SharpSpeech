<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import type { TtsModel, SynthesisRequest, SynthesisMetrics, TextPreset, StatusType } from './types'

import AppHeader from './components/AppHeader.vue'
import ModelSelector from './components/ModelSelector.vue'
import SpeakerSelector from './components/SpeakerSelector.vue'
import LanguageSelector from './components/LanguageSelector.vue'
import TextInput from './components/TextInput.vue'
import ParameterControls from './components/ParameterControls.vue'
import AudioPlayer from './components/AudioPlayer.vue'
import StatusMessage from './components/StatusMessage.vue'

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

const presets: TextPreset[] = [
  {
    id: 'freeform',
    label: 'Freeform',
    text: 'This text-to-speech system runs entirely in your browser, providing fast and private operation without sending any data to external servers.'
  },
  {
    id: 'quote',
    label: 'Quote',
    text: '"The only way to do great work is to love what you do." - Steve Jobs'
  },
  {
    id: 'paragraph',
    label: 'Paragraph',
    text: 'In the rapidly evolving landscape of artificial intelligence, text-to-speech technology has emerged as a transformative tool for accessibility and user experience. Modern TTS systems leverage deep learning models to produce increasingly natural and expressive speech synthesis, bridging the gap between written content and auditory communication.'
  },
  {
    id: 'script',
    label: 'Script',
    text: 'Welcome to our demonstration of advanced speech synthesis! Today, we will explore how machine learning enables computers to speak with remarkable clarity and emotion.'
  }
]

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

onMounted(async () => {
  await loadModels()
})
</script>

<template>
  <main class="app">
    <div class="demo-container">
      <div class="demo-content">
        <AppHeader title="FastTTSR" subtitle="Multi-Model Text-to-Speech System" />

        <div class="demo-controls">
          <ModelSelector v-model="form.model" :models="models" @update:model-value="syncModelDefaults" />
          
          <SpeakerSelector 
            v-if="selectedModel?.speakers && selectedModel.speakers.length > 0"
            v-model="form.speaker" 
            :speakers="selectedModel.speakers" 
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

@media (max-width: 768px) {
  .demo-container {
    padding: 1rem;
  }
  
  .demo-content {
    gap: 1rem;
  }
}
</style>
