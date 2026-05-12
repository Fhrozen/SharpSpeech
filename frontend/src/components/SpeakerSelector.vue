<script setup lang="ts">
import { ref } from 'vue'
import type { SpeakerMetadata } from '../types'

const props = defineProps<{
  speakers: string[]
  modelValue: string
  speakerMetadata?: SpeakerMetadata[]
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const hoveredSpeaker = ref<string | null>(null)
const tooltipPosition = ref({ top: 0, left: 0 })

const formatSpeakerName = (speaker: string): string => {
  // Check if we have metadata for this speaker
  const metadata = props.speakerMetadata?.find(m => m.id === speaker)
  if (metadata) {
    return `${metadata.name} (${speaker})`
  }
  
  // Remove gender prefix (af_, am_) and capitalize
  return speaker
    .replace(/^(af|am)_/, '')
    .split('_')
    .map(word => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ')
}

const getSpeakerDescription = (speaker: string): string => {
  const metadata = props.speakerMetadata?.find(m => m.id === speaker)
  return metadata?.description || ''
}

const handleMouseEnter = (speaker: string, event: MouseEvent) => {
  const description = getSpeakerDescription(speaker)
  if (description) {
    hoveredSpeaker.value = speaker
    updateTooltipPosition(event)
  }
}

const handleMouseMove = (event: MouseEvent) => {
  if (hoveredSpeaker.value) {
    updateTooltipPosition(event)
  }
}

const handleMouseLeave = () => {
  hoveredSpeaker.value = null
}

const updateTooltipPosition = (event: MouseEvent) => {
  tooltipPosition.value = {
    top: event.clientY + 15,
    left: event.clientX + 15
  }
}
</script>

<template>
  <div class="demo-param">
    <div class="speaker-container">
      <label class="speaker-label">Speaker: </label>
      <div class="speaker-list">
        <template v-for="(speaker, idx) in speakers" :key="speaker">
          <span 
            class="speaker-item" 
            :class="{ active: modelValue === speaker }"
            @click="emit('update:modelValue', speaker)"
            @mouseenter="(e) => handleMouseEnter(speaker, e)"
            @mousemove="handleMouseMove"
            @mouseleave="handleMouseLeave"
          >
            {{ formatSpeakerName(speaker) }}
          </span>
          <span v-if="idx < speakers.length - 1" class="speaker-separator">, </span>
        </template>
      </div>
    </div>

    <!-- Tooltip -->
    <Teleport to="body">
      <div 
        v-if="hoveredSpeaker" 
        class="speaker-tooltip"
        :style="{
          top: tooltipPosition.top + 'px',
          left: tooltipPosition.left + 'px'
        }"
      >
        {{ getSpeakerDescription(hoveredSpeaker) }}
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.demo-param {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.speaker-container {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0.25rem;
}

.speaker-label {
  font-weight: 400;
  color: #ffffff;
  font-size: 1rem;
  margin: 0;
}

.speaker-list {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
}

.speaker-item {
  color: #888888;
  cursor: pointer;
  transition: all 0.2s;
  font-size: 0.95rem;
  padding: 0;
  background: transparent;
  border: none;
  font-weight: 400;
  text-decoration: none;
  border-bottom: 1px solid transparent;
  padding-bottom: 1px;
}

.speaker-item:hover {
  color: #ffffff;
}

.speaker-item.active {
  color: #ffffff;
  font-weight: 400;
  border-bottom-color: #ffffff;
}

.speaker-separator {
  color: #555555;
  user-select: none;
  margin: 0 0.125rem;
}

.speaker-tooltip {
  position: fixed;
  background: rgba(0, 0, 0, 0.95);
  color: #ffffff;
  padding: 0.75rem 1rem;
  border-radius: 8px;
  font-size: 0.875rem;
  max-width: 300px;
  z-index: 10000;
  pointer-events: none;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
  border: 1px solid rgba(255, 255, 255, 0.1);
  line-height: 1.4;
}
</style>
