<script setup lang="ts">
defineProps<{
  speakers: string[]
  modelValue: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const formatSpeakerName = (speaker: string): string => {
  return speaker
    .replace(/^(af|am)_/, (_, prefix) => (prefix === 'af' ? '♀️ ' : '♂️ '))
    .split('_')
    .map(word => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ')
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
          >
            {{ formatSpeakerName(speaker) }}
          </span>
          <span v-if="idx < speakers.length - 1" class="speaker-separator">, </span>
        </template>
      </div>
    </div>
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
  transition: color 0.2s;
  font-size: 0.95rem;
  padding: 0;
  background: transparent;
  border: none;
  font-weight: 400;
}

.speaker-item:hover {
  color: #ffffff;
}

.speaker-item.active {
  color: #ffffff;
  font-weight: 500;
}

.speaker-separator {
  color: #555555;
  user-select: none;
  margin: 0 0.125rem;
}
</style>
