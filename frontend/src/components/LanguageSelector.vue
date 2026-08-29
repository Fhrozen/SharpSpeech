<script setup lang="ts">
defineProps<{
  languages: string[]
  modelValue: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const languageMap: Record<string, string> = {
  'auto': 'Auto-detect',
  'en': 'English',
  'en-us': 'English',
  'ja': 'Japanese',
  'ja-jp': 'Japanese',
  'ko': 'Korean',
  'ko-kr': 'Korean',
  'es': 'Spanish',
  'es-es': 'Spanish',
  'fr': 'French',
  'fr-fr': 'French',
  'de': 'German',
  'de-de': 'German',
  'it': 'Italian',
  'it-it': 'Italian',
  'pt': 'Portuguese',
  'pt-br': 'Portuguese',
  'zh': 'Chinese',
  'zh-cn': 'Chinese',
  'ar': 'Arabic',
  'bg': 'Bulgarian',
  'cs': 'Czech',
  'da': 'Danish',
  'el': 'Greek',
  'et': 'Estonian',
  'fi': 'Finnish',
  'hi': 'Hindi',
  'hr': 'Croatian',
  'hu': 'Hungarian',
  'id': 'Indonesian',
  'lt': 'Lithuanian',
  'lv': 'Latvian',
  'nl': 'Dutch',
  'no': 'Norwegian',
  'pl': 'Polish',
  'ro': 'Romanian',
  'ru': 'Russian',
  'sk': 'Slovak',
  'sl': 'Slovenian',
  'sv': 'Swedish',
  'tr': 'Turkish',
  'uk': 'Ukrainian',
  'vi': 'Vietnamese'
}

const formatLanguageName = (lang: string): string => {
  const normalized = lang.toLowerCase()
  return languageMap[normalized] || lang
    .split('-')
    .map(part => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}
</script>

<template>
  <div class="demo-param language-section">
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

.language-section {
  margin-top: 0.5rem;
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

.language-item:hover {
  color: #ffffff;
}

.language-item.active {
  color: #ffffff;
  font-weight: 400;
  border-bottom-color: #ffffff;
}

.language-separator {
  color: #555555;
  user-select: none;
  margin: 0 0.125rem;
}
</style>
