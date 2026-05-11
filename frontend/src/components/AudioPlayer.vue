<script setup lang="ts">
import { ref, watch, type Ref } from 'vue'
import type { SynthesisMetrics } from '../types'

const props = defineProps<{
  audioUrl: string | null
  metrics: SynthesisMetrics
  loading?: boolean
}>()

const emit = defineEmits<{
  download: []
}>()

const audioElement: Ref<HTMLAudioElement | null> = ref(null)
const isPlaying = ref(false)

const togglePlayPause = () => {
  if (!audioElement.value) return
  
  if (isPlaying.value) {
    audioElement.value.pause()
  } else {
    audioElement.value.play()
  }
}

const handlePlay = () => {
  isPlaying.value = true
}

const handlePause = () => {
  isPlaying.value = false
}

const handleEnded = () => {
  isPlaying.value = false
}

watch(() => props.audioUrl, (newUrl) => {
  if (newUrl && audioElement.value) {
    // Reset playing state when new audio is loaded
    isPlaying.value = false
  }
})
</script>

<template>
  <div v-if="!audioUrl && !loading" class="demo-placeholder">
    <div class="demo-placeholder-icon">🎙️</div>
    <p>Your generated speech will appear here</p>
  </div>

  <div v-if="audioUrl" class="audio-result-container">
    <!-- Metrics Display -->
    <div class="audio-result-metrics">
      <div class="metric">
        <span class="metric-value">{{ metrics.processingTime }}</span>
        <span class="metric-label">Processing Time ↓</span>
      </div>
      <div class="metric">
        <span class="metric-value">{{ metrics.charsPerSecond }}</span>
        <span class="metric-label">Chars/sec ↑</span>
      </div>
      <div class="metric">
        <span class="metric-value">{{ metrics.rtf }}</span>
        <span class="metric-label">RTF ↓</span>
      </div>
    </div>

    <!-- Audio Player -->
    <div class="audio-player-container">
      <button @click="togglePlayPause" class="play-button" :class="{ playing: isPlaying }">
        <svg v-if="!isPlaying" width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
          <path d="M8 5v14l11-7z"/>
        </svg>
        <svg v-else width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
          <path d="M6 4h4v16H6V4zm8 0h4v16h-4V4z"/>
        </svg>
      </button>

      <div class="waveform-visualization">
        <div class="waveform-bars" :class="{ active: isPlaying }">
          <span v-for="i in 40" :key="i" class="bar"></span>
        </div>
        <audio 
          ref="audioElement"
          :src="audioUrl"
          @play="handlePlay"
          @pause="handlePause"
          @ended="handleEnded"
          class="hidden-audio"
        ></audio>
      </div>

      <button @click="emit('download')" class="download-btn" title="Download audio file">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
          <polyline points="7 10 12 15 17 10"></polyline>
          <line x1="12" y1="15" x2="12" y2="3"></line>
        </svg>
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

.audio-result-container {
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
  justify-content: flex-end;
  gap: 1.5rem;
  flex-wrap: wrap;
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

.audio-player-container {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.play-button {
  background: transparent;
  border: 1px solid #333333;
  color: #888888;
  width: 40px;
  height: 40px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: all 0.2s;
  flex-shrink: 0;
}

.play-button:hover,
.play-button.playing {
  border-color: #fbbf24;
  color: #fbbf24;
}

.waveform-visualization {
  flex: 1;
  min-width: 0;
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;
}

.hidden-audio {
  display: none;
}

.waveform-bars {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 2px;
  height: 100%;
  width: 100%;
}

.bar {
  width: 3px;
  height: 20%;
  background: #888888;
  border-radius: 2px;
  transition: all 0.3s ease;
}

.waveform-bars.active .bar {
  background: #fbbf24;
  animation: wave 1.2s ease-in-out infinite;
}

.waveform-bars.active .bar:nth-child(2n) {
  animation-delay: 0.1s;
}

.waveform-bars.active .bar:nth-child(3n) {
  animation-delay: 0.2s;
}

.waveform-bars.active .bar:nth-child(4n) {
  animation-delay: 0.3s;
}

.waveform-bars.active .bar:nth-child(5n) {
  animation-delay: 0.4s;
}

@keyframes wave {
  0%, 100% {
    height: 20%;
  }
  50% {
    height: 80%;
  }
}

.download-btn {
  background: transparent;
  border: 1px solid #333333;
  color: #888888;
  width: 40px;
  height: 40px;
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
</style>
