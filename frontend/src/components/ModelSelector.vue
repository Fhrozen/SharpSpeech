<script setup lang="ts">
import type { TtsModel } from '../types'

defineProps<{
  models: TtsModel[]
  modelValue: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()
</script>

<template>
  <div class="demo-param">
    <label class="speaker-label">Model: </label>
    <select 
      :value="modelValue" 
      @change="emit('update:modelValue', ($event.target as HTMLSelectElement).value)" 
      class="model-select"
    >
      <option v-for="model in models" :key="model.name" :value="model.name">
        {{ model.displayName }}
      </option>
    </select>
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

.model-select {
  background: transparent;
  color: #ffffff;
  border: 1px solid #333333;
  border-radius: 0.25rem;
  padding: 0.625rem 0.875rem;
  font-size: 0.95rem;
  outline: none;
  cursor: pointer;
  transition: border-color 0.2s;
}

.model-select:hover {
  border-color: #555555;
}

.model-select:focus {
  border-color: #fbbf24;
}
</style>
