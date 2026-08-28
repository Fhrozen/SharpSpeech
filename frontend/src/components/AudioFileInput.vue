<script setup lang="ts">
defineProps<{
  modelValue: File | null
}>()

const emit = defineEmits<{
  'update:modelValue': [value: File | null]
}>()

function onChange(event: Event) {
  const input = event.target as HTMLInputElement
  emit('update:modelValue', input.files?.[0] ?? null)
}

function clear() {
  emit('update:modelValue', null)
}
</script>

<template>
  <div class="demo-param">
    <label class="speaker-label">Audio file: </label>
    <div class="file-input-row">
      <label class="file-input-btn">
        <input type="file" accept="audio/*" @change="onChange" class="file-input-hidden" />
        Choose file
      </label>
      <span v-if="modelValue" class="file-name">{{ modelValue.name }}</span>
      <span v-else class="file-name file-name-empty">No file selected</span>
      <button v-if="modelValue" type="button" class="file-clear-btn" @click="clear" title="Remove file">✕</button>
    </div>
  </div>
</template>

<style scoped>
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

.file-input-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.file-input-btn {
  background: #000000;
  color: #ffffff;
  border: 1px solid #333333;
  border-radius: 0.25rem;
  padding: 0.625rem 1rem;
  font-size: 0.95rem;
  cursor: pointer;
  transition: border-color 0.2s;
}

.file-input-btn:hover {
  border-color: #555555;
}

.file-input-hidden {
  display: none;
}

.file-name {
  color: #ffffff;
  font-size: 0.9rem;
  font-family: monospace;
}

.file-name-empty {
  color: #555555;
}

.file-clear-btn {
  background: transparent;
  border: none;
  color: #f87171;
  cursor: pointer;
  font-size: 1rem;
  padding: 0.25rem;
}
</style>
