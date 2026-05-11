<template>
  <main class="app">
    <h1>FastTTSR</h1>
    <p>OpenAI-compatible TTS endpoint with model switcher.</p>

    <label>
      Model
      <select v-model="form.model" @change="syncModelDefaults">
        <option v-for="model in models" :key="model.name" :value="model.name">{{ model.displayName }}</option>
      </select>
    </label>

    <label>
      Speaker
      <select v-model="form.speaker">
        <option v-for="speaker in selectedModel?.speakers ?? []" :key="speaker" :value="speaker">{{ speaker }}</option>
      </select>
    </label>

    <label>
      Language
      <select v-model="form.language">
        <option v-for="lang in selectedModel?.supportedLanguages ?? []" :key="lang" :value="lang">{{ lang }}</option>
      </select>
    </label>

    <label>
      Speed
      <input v-model.number="form.speed" type="number" step="0.1" min="0.5" max="2" />
    </label>

    <label>
      Text
      <textarea v-model="form.input" rows="7"></textarea>
    </label>

    <button :disabled="loading" @click="synthesize">{{ loading ? 'Generating...' : 'Generate' }}</button>

    <audio v-if="audioUrl" :src="audioUrl" controls></audio>
    <p v-if="error" class="error">{{ error }}</p>
  </main>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'

const models = ref([])
const audioUrl = ref('')
const error = ref('')
const loading = ref(false)
const form = reactive({
  model: 'kokoro-tts',
  speaker: '',
  language: '',
  speed: 1,
  input: 'Hello from FastTTSR'
})

const selectedModel = computed(() => models.value.find((m) => m.name === form.model))

function syncModelDefaults() {
  const current = selectedModel.value
  if (!current) return
  form.speaker = current.speakers[0] ?? ''
  form.language = current.supportedLanguages[0] ?? ''
}

async function loadModels() {
  const response = await fetch('/api/models')
  models.value = await response.json()
  if (!models.value.length) return
  if (!models.value.some((m) => m.name === form.model)) {
    form.model = models.value[0].name
  }
  syncModelDefaults()
}

async function synthesize() {
  loading.value = true
  error.value = ''

  try {
    const response = await fetch('/v1/audio/speech', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        model: form.model,
        input: form.input,
        speed: form.speed,
        speaker: form.speaker,
        language: form.language,
        response_format: 'wav',
        voice: form.speaker || 'default'
      })
    })

    if (!response.ok) {
      const payload = await response.json()
      throw new Error(payload.message || 'Request failed')
    }

    const blob = await response.blob()
    audioUrl.value = URL.createObjectURL(blob)
  } catch (err) {
    error.value = err.message
  } finally {
    loading.value = false
  }
}

onMounted(loadModels)
</script>

<style>
body {
  margin: 0;
  font-family: Inter, sans-serif;
  background: #0f172a;
  color: #f8fafc;
}

.app {
  max-width: 820px;
  margin: 2rem auto;
  padding: 1.5rem;
  background: #111827;
  border-radius: 0.75rem;
  display: grid;
  gap: 1rem;
}

label {
  display: grid;
  gap: 0.35rem;
}

textarea,
input,
select,
button {
  border-radius: 0.4rem;
  border: 1px solid #334155;
  padding: 0.6rem 0.7rem;
  background: #1e293b;
  color: inherit;
}

button {
  cursor: pointer;
  background: #2563eb;
  border: none;
}

.error {
  color: #f87171;
}
</style>
