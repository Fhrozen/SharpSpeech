<script setup lang="ts">
import { ref, computed, onMounted, nextTick, type Ref } from 'vue'
import type { TextPreset } from '../types'

const props = withDefaults(defineProps<{
  modelValue: string
  presets: TextPreset[]
  minCharCount?: number
}>(), {
  minCharCount: 10
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const textInput: Ref<HTMLDivElement | null> = ref(null)
const currentPreset = ref('freeform')
const charCount = computed(() => props.modelValue.length)

const applyPreset = (presetId: string) => {
  currentPreset.value = presetId
  const preset = props.presets.find(p => p.id === presetId)
  if (preset && textInput.value) {
    textInput.value.innerText = preset.text
    emit('update:modelValue', preset.text)
  }
}

const handleInput = (e: Event) => {
  const target = e.target as HTMLDivElement
  emit('update:modelValue', target.innerText)
}

onMounted(async () => {
  await nextTick()
  if (textInput.value && props.presets.length > 0) {
    textInput.value.innerText = props.presets[0].text
    emit('update:modelValue', props.presets[0].text)
  }
})
</script>

<template>
  <div class="text-input-section">
    <div class="demo-text-input-container">
      <div
        ref="textInput"
        contenteditable="true"
        @input="handleInput"
        class="demo-text-input"
        placeholder="Enter text to synthesize..."
      ></div>
      
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
  </div>
</template>

<style scoped>
.text-input-section {
  width: 100%;
}

.demo-text-input-container {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.demo-text-input {
  background: transparent;
  color: #ffffff;
  border: none;
  border-left: 4px solid #ffffff;
  padding: 0.75rem 1rem;
  font-size: 0.95rem;
  min-height: 120px;
  outline: none;
  font-family: inherit;
  line-height: 1.6;
  overflow-y: auto;
  transition: border-color 0.2s;
}

.demo-text-input:focus {
  border-left-color: #fbbf24;
}

.demo-text-input:empty::before {
  content: attr(placeholder);
  color: #555555;
}

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
  color: #888888;
  display: flex;
  align-items: center;
  margin-right: 0.5rem;
}

.preset-item {
  color: #888888;
  cursor: pointer;
  transition: color 0.2s;
  font-size: 0.875rem;
  padding: 0.25rem 0.5rem;
  background: transparent;
  border: none;
  font-weight: 400;
}

.preset-item:hover {
  color: #ffffff;
}

.preset-item.active {
  color: #fbbf24;
  font-weight: 500;
}

.demo-char-counter {
  font-size: 0.875rem;
  color: #888888;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.demo-char-counter .warning {
  color: #ff6b6b;
}

.demo-char-warning {
  color: #ff6b6b;
  font-size: 0.75rem;
}
</style>
