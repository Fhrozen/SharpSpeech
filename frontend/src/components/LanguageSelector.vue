<script setup lang="ts">
defineProps<{
  languages: string[]
  modelValue: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const formatLanguageName = (lang: string): string => {
  return lang
    .split('-')
    .map(part => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}
</script>

<template>
  <div class="demo-param">
    <div class="language-container">
      <label class="language-label">Language: </label>
      <div class="language-list">
        <template v-for="(lang, idx) in languages" :key="lang">
          <span 
            class="language-item" 
            :class="{ active: modelValue === lang }"
            @click="emit('update:modelValue', lang)"
          >
            {{ formatLanguageName(lang) }}
          </span>
          <span v-if="idx < languages.length - 1" class="language-separator">, </span>
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

.language-container {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0.25rem;
}

.language-label {
  font-weight: 400;
  color: #ffffff;
  font-size: 1rem;
  margin: 0;
}

.language-list {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
}

.language-item {
  color: #888888;
  cursor: pointer;
  transition: color 0.2s;
  font-size: 0.95rem;
  padding: 0;
  background: transparent;
  border: none;
  font-weight: 400;
}

.language-item:hover {
  color: #ffffff;
}

.language-item.active {
  color: #ffffff;
  font-weight: 500;
}

.language-separator {
  color: #555555;
  user-select: none;
  margin: 0 0.125rem;
}
</style>
