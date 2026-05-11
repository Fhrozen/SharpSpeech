<script setup lang="ts">
import type { StatusType } from '../types'

defineProps<{
  message: string
  title: string
  type: StatusType
}>()
</script>

<template>
  <div v-if="message" class="demo-status-box" :class="`status-${type}`">
    <div class="demo-status-content">
      <div class="demo-status-text">
        <strong>{{ title }}</strong> {{ message }}
      </div>
    </div>
  </div>
</template>

<style scoped>
.demo-status-box {
  padding: 1rem 1.5rem;
  border-radius: 0.25rem;
  border: none;
  animation: fadeIn 0.3s;
  font-family: monospace;
}

@keyframes fadeIn {
  from {
    opacity: 0;
    transform: translateY(-10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.status-loading {
  background: rgba(59, 130, 246, 0.1);
  color: #60a5fa;
  border: 1px solid rgba(59, 130, 246, 0.2);
}

.status-success {
  background: rgba(34, 197, 94, 0.1);
  color: #4ade80;
  border: 1px solid rgba(34, 197, 94, 0.2);
}

.status-error {
  background: rgba(239, 68, 68, 0.1);
  color: #f87171;
  border: 1px solid rgba(239, 68, 68, 0.2);
}

.demo-status-content {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.demo-status-text {
  flex: 1;
}

.demo-status-text strong {
  font-weight: 600;
  margin-right: 0.5rem;
}
</style>
