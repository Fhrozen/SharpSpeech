<script setup lang="ts">
import { ref } from 'vue'
import type { TranscriptionMetrics, TranscriptionSegment } from '../types'

const props = defineProps<{
  text: string | null
  metrics: TranscriptionMetrics
  segments?: TranscriptionSegment[]
  loading?: boolean
}>()

const copied = ref(false)

function formatTimestamp(seconds: number): string {
  const minutes = Math.floor(seconds / 60)
  const remainder = (seconds % 60).toFixed(1)
  return `${minutes}:${remainder.padStart(4, '0')}`
}

async function copyText() {
  if (!props.text) return
  try {
    await navigator.clipboard.writeText(props.text)
    copied.value = true
    setTimeout(() => {
      copied.value = false
    }, 2000)
  } catch {
    // Clipboard API unavailable (e.g. insecure context) - ignore, copy is a convenience only.
  }
}
</script>

<template>
  <div v-if="!text && !loading" class="demo-placeholder">
    <div class="demo-placeholder-icon">📝</div>
    <p>Your transcription will appear here</p>
  </div>

  <div v-if="text" class="transcription-result-container">
    <div class="audio-result-metrics">
      <div class="metric">
        <span class="metric-value">{{ metrics.processingTime }}</span>
        <span class="metric-label">Processing Time ↓</span>
      </div>
      <div class="metric">
        <span class="metric-value">{{ metrics.rtf }}</span>
        <span class="metric-label">RTF ↓</span>
      </div>
      <div class="metric">
        <span class="metric-value">{{ metrics.audioDuration }}</span>
        <span class="metric-label">Audio Duration</span>
      </div>
    </div>

    <div v-if="segments && segments.length > 0" class="transcription-segments">
      <div v-for="segment in segments" :key="segment.id" class="transcription-segment-row">
        <span class="segment-timestamp">{{ formatTimestamp(segment.start) }}–{{ formatTimestamp(segment.end) }}</span>
        <span class="segment-text">{{ segment.text }}</span>
      </div>
      <div class="transcription-text-row">
        <button class="copy-btn" @click="copyText" :title="copied ? 'Copied!' : 'Copy full text'">
          {{ copied ? '✓' : '⍧' }} Copy full text
        </button>
      </div>
    </div>

    <div v-else class="transcription-text-row">
      <p class="transcription-text">{{ text }}</p>
      <button class="copy-btn" @click="copyText" :title="copied ? 'Copied!' : 'Copy text'">
        {{ copied ? '✓' : '⧉' }}
      </button>
    </div>
  </div>
</template>

<style scoped>
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

.transcription-result-container {
  width: 100%;
  background: #1a1a1a;
  border-radius: 0.25rem;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.audio-result-metrics {
  display: flex;
  gap: 1.5rem;
  justify-content: center;
  flex-wrap: wrap;
}

.metric {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.25rem;
}

.metric-value {
  font-size: 1.1rem;
  font-weight: 600;
  color: #fbbf24;
  font-family: monospace;
}

.metric-label {
  font-size: 0.75rem;
  color: #888888;
}

.transcription-text-row {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  background: #000000;
  border: 1px solid #333333;
  border-radius: 0.25rem;
  padding: 1rem;
}

.transcription-text {
  margin: 0;
  flex: 1;
  color: #ffffff;
  font-family: monospace;
  white-space: pre-wrap;
  word-break: break-word;
}

.copy-btn {
  background: transparent;
  border: 1px solid #333333;
  border-radius: 0.25rem;
  color: #ffffff;
  cursor: pointer;
  padding: 0.4rem 0.6rem;
  font-size: 0.9rem;
  flex-shrink: 0;
}

.copy-btn:hover {
  border-color: #fbbf24;
  color: #fbbf24;
}

.transcription-segments {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.transcription-segment-row {
  display: flex;
  align-items: baseline;
  gap: 0.75rem;
  background: #000000;
  border: 1px solid #333333;
  border-radius: 0.25rem;
  padding: 0.6rem 0.75rem;
}

.segment-timestamp {
  flex-shrink: 0;
  color: #fbbf24;
  font-family: monospace;
  font-size: 0.8rem;
}

.segment-text {
  color: #ffffff;
  font-family: monospace;
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
