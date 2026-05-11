<script setup lang="ts">
withDefaults(defineProps<{
  speed: number
  quality?: number
  showQuality: boolean
  speedMin?: number
  speedMax?: number
  loading?: boolean
  canGenerate?: boolean
}>(), {
  speedMin: 0.5,
  speedMax: 2.0,
  loading: false,
  canGenerate: false
})

const emit = defineEmits<{
  'update:speed': [value: number]
  'update:quality': [value: number]
  generate: []
}>()
</script>

<template>
  <div class="demo-output-section">
    <div class="demo-params-row">
      <!-- Quality Slider (only for models that support it) -->
      <div v-if="showQuality" class="demo-param">
        <div class="demo-param-header">
          <label>
            Quality: <span class="param-value">{{ quality }} Steps</span>
          </label>
        </div>
        <input 
          type="range" 
          :value="quality" 
          @input="emit('update:quality', Number(($event.target as HTMLInputElement).value))"
          min="2" 
          max="16" 
          step="1"
          class="demo-slider"
        />
      </div>

      <!-- Speed Slider -->
      <div class="demo-param">
        <div class="demo-param-header">
          <label>
            Speech Speed: <span class="param-value">{{ speed.toFixed(1) }}x</span>
          </label>
        </div>
        <input 
          type="range" 
          :value="speed" 
          @input="emit('update:speed', Number(($event.target as HTMLInputElement).value))"
          :min="speedMin" 
          :max="speedMax" 
          step="0.05"
          class="demo-slider"
        />
      </div>

      <!-- Generate Button -->
      <button 
        class="demo-generate-btn" 
        :disabled="!canGenerate"
        @click="emit('generate')"
      >
        <span class="icon">⚡</span>
        <span class="text">{{ loading ? 'Generating...' : 'Generate Speech' }}</span>
        <span class="shimmer" aria-hidden="true"></span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.demo-output-section {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.demo-params-row {
  display: grid;
  grid-template-columns: 1fr 1fr auto;
  gap: 1.5rem;
  align-items: end;
}

.demo-param {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.demo-param-header {
  margin-bottom: 0.75rem;
}

.demo-param-header label {
  font-size: 0.95rem;
  color: #ffffff;
  font-weight: 400;
}

.param-value {
  color: #fbbf24;
  font-weight: 500;
  font-family: monospace;
}

.demo-slider {
  -webkit-appearance: none;
  appearance: none;
  width: 100%;
  height: 3px;
  background: #333333;
  outline: none;
  border-radius: 2px;
  cursor: pointer;
}

.demo-slider::-webkit-slider-thumb {
  -webkit-appearance: none;
  appearance: none;
  width: 14px;
  height: 14px;
  background: #fbbf24;
  border-radius: 50%;
  cursor: pointer;
  transition: transform 0.1s;
}

.demo-slider::-webkit-slider-thumb:hover {
  transform: scale(1.2);
}

.demo-slider::-moz-range-thumb {
  width: 14px;
  height: 14px;
  background: #fbbf24;
  border: none;
  border-radius: 50%;
  cursor: pointer;
  transition: transform 0.1s;
}

.demo-slider::-moz-range-thumb:hover {
  transform: scale(1.2);
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
  position: relative;
  overflow: hidden;
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

.demo-generate-btn .icon {
  font-size: 1.1rem;
}

.demo-generate-btn .shimmer {
  position: absolute;
  top: 0;
  left: -100%;
  width: 100%;
  height: 100%;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.1), transparent);
  animation: shimmer 2s infinite;
}

@keyframes shimmer {
  100% {
    left: 100%;
  }
}

@media (max-width: 768px) {
  .demo-params-row {
    grid-template-columns: 1fr;
  }
  
  .demo-generate-btn {
    width: 100%;
    justify-content: center;
  }
}
</style>
