<script setup lang="ts">
withDefaults(defineProps<{
  icon?: string
  title: string
  subtitle?: string
}>(), {
  icon: '⚡'
})
</script>

<template>
  <div class="demo-header-wrapper">
    <span class="demo-header-icon">{{ icon }}</span>
    <h2 class="demo-header">
      <span class="demo-header-text">
        <span class="demo-header-bold">{{ title }}</span>
        <template v-if="subtitle"> | {{ subtitle }}</template>
      </span>
    </h2>
    <a href="/swagger" target="_blank" class="swagger-link" title="API Documentation">
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
        <polyline points="14 2 14 8 20 8"></polyline>
        <line x1="16" y1="13" x2="8" y2="13"></line>
        <line x1="16" y1="17" x2="8" y2="17"></line>
        <polyline points="10 9 9 9 8 9"></polyline>
      </svg>
      <span>API Docs</span>
    </a>
  </div>
</template>

<style scoped>
.demo-header-wrapper {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
  justify-content: space-between;
}

.demo-header-icon {
  font-size: 2rem;
  filter: none;
}

.demo-header {
  margin: 0;
  font-size: 1.5rem;
  font-weight: 400;
  color: #ffffff;
  letter-spacing: -0.01em;
  flex: 1;
}

.demo-header-text {
  display: inline;
}

.demo-header-bold {
  font-weight: 600;
}

.swagger-link {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: #888888;
  text-decoration: none;
  font-size: 0.875rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #333333;
  border-radius: 0.25rem;
  transition: all 0.2s;
}

.swagger-link:hover {
  color: #fbbf24;
  border-color: #fbbf24;
}

.swagger-link svg {
  flex-shrink: 0;
}
</style>
