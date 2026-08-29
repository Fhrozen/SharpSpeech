<script setup lang="ts">
import { ref, onBeforeUnmount } from 'vue'
import type { StreamMessage } from '../types'

const props = defineProps<{
  model: string
  language: string
  enableVad: boolean
}>()

const isRecording = ref(false)
const liveText = ref('')
const statusMessage = ref('')
const errorMessage = ref('')

let mediaStream: MediaStream | null = null
let audioContext: AudioContext | null = null
let workletNode: AudioWorkletNode | null = null
let sourceNode: MediaStreamAudioSourceNode | null = null
let socket: WebSocket | null = null

const workletUrl = new URL('../audio-worklets/pcm-capture-processor.js', import.meta.url)

async function startMicrophone() {
  errorMessage.value = ''
  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
    await startStreaming(stream)
  } catch (err) {
    errorMessage.value = `Could not access microphone: ${(err as Error).message}`
  }
}

async function startTabAudio() {
  errorMessage.value = ''
  if (!navigator.mediaDevices.getDisplayMedia) {
    errorMessage.value = 'This browser does not support capturing tab/system audio.'
    return
  }

  try {
    const stream = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: true })
    if (stream.getAudioTracks().length === 0) {
      stream.getTracks().forEach(track => track.stop())
      errorMessage.value = 'No audio was shared - retry and make sure to enable "Share tab/system audio".'
      return
    }

    stream.getVideoTracks().forEach(track => track.stop())
    await startStreaming(stream)
  } catch (err) {
    errorMessage.value = `Could not capture tab/system audio: ${(err as Error).message}`
  }
}

async function startStreaming(stream: MediaStream) {
  mediaStream = stream
  liveText.value = ''
  statusMessage.value = 'Connecting...'

  audioContext = new AudioContext({ sampleRate: 16000 })
  await audioContext.audioWorklet.addModule(workletUrl)

  sourceNode = audioContext.createMediaStreamSource(stream)
  workletNode = new AudioWorkletNode(audioContext, 'pcm-capture-processor')
  sourceNode.connect(workletNode)

  const params = new URLSearchParams({ model: props.model })
  if (props.language) {
    params.set('language', props.language)
  }
  if (props.enableVad) {
    params.set('use_vad', 'true')
  }

  const wsProtocol = window.location.protocol === 'https:' ? 'wss' : 'ws'
  socket = new WebSocket(`${wsProtocol}://${window.location.host}/v1/audio/transcriptions/stream?${params.toString()}`)
  socket.binaryType = 'arraybuffer'

  socket.onopen = () => {
    isRecording.value = true
    statusMessage.value = 'Listening...'
    if (workletNode) {
      workletNode.port.onmessage = (event: MessageEvent<ArrayBuffer>) => {
        if (socket && socket.readyState === WebSocket.OPEN) {
          socket.send(event.data)
        }
      }
    }
  }

  socket.onmessage = (event) => {
    try {
      const message = JSON.parse(event.data) as StreamMessage
      liveText.value = message.text
      if (message.type === 'final') {
        statusMessage.value = 'Finished'
      }
    } catch {
      // Ignore malformed frames.
    }
  }

  socket.onerror = () => {
    errorMessage.value = 'Streaming connection failed - the server may require in-process ASR mode.'
  }

  socket.onclose = () => {
    isRecording.value = false
    cleanupAudio()
  }
}

function stop() {
  if (socket && socket.readyState === WebSocket.OPEN) {
    socket.send(JSON.stringify({ type: 'end' }))
  }
  cleanupAudio()
}

function cleanupAudio() {
  workletNode?.disconnect()
  sourceNode?.disconnect()
  mediaStream?.getTracks().forEach(track => track.stop())
  audioContext?.close()
  workletNode = null
  sourceNode = null
  mediaStream = null
  audioContext = null
}

onBeforeUnmount(() => {
  stop()
  socket?.close()
})
</script>

<template>
  <div class="live-transcription">
    <div class="live-controls">
      <button v-if="!isRecording" type="button" class="live-btn" @click="startMicrophone">
        🎤 Start Microphone
      </button>
      <button v-if="!isRecording" type="button" class="live-btn" @click="startTabAudio">
        🖥️ Start Tab/System Audio
      </button>
      <button v-if="isRecording" type="button" class="live-btn live-btn-stop" @click="stop">
        ⏹ Stop
      </button>
    </div>

    <div v-if="statusMessage" class="live-status">{{ statusMessage }}</div>
    <div v-if="errorMessage" class="live-error">{{ errorMessage }}</div>

    <div v-if="liveText || isRecording" class="live-text-box">
      {{ liveText || 'Listening for speech...' }}
    </div>
  </div>
</template>

<style scoped>
.live-transcription {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.live-controls {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.live-btn {
  background: #000000;
  color: #ffffff;
  border: 1px solid #333333;
  border-radius: 0.25rem;
  padding: 0.625rem 1rem;
  font-size: 0.95rem;
  cursor: pointer;
  transition: border-color 0.2s;
}

.live-btn:hover {
  border-color: #fbbf24;
}

.live-btn-stop {
  border-color: #fbbf24;
  color: #fbbf24;
}

.live-status {
  color: #888888;
  font-size: 0.85rem;
}

.live-error {
  color: #f87171;
  font-size: 0.9rem;
}

.live-text-box {
  background: #0a0a0a;
  border: 1px solid #333333;
  border-radius: 0.25rem;
  padding: 1rem;
  min-height: 3rem;
  color: #ffffff;
  white-space: pre-wrap;
}
</style>
