// Captures mono float audio from the main input, resamples it to 16kHz (linear interpolation,
// matching the server's own resampling approach) and posts raw 16-bit PCM back to the main thread
// for sending over the streaming ASR WebSocket - the server expects a plain PCM16 wire format with
// no WAV/container header.
class PcmCaptureProcessor extends AudioWorkletProcessor {
  constructor() {
    super()
    this.targetSampleRate = 16000
    this.resampleRatio = sampleRate / this.targetSampleRate
  }

  process(inputs) {
    const input = inputs[0]
    const channelData = input && input[0]
    if (!channelData || channelData.length === 0) {
      return true
    }

    const outLength = Math.max(0, Math.floor(channelData.length / this.resampleRatio))
    if (outLength === 0) {
      return true
    }

    const pcm16 = new Int16Array(outLength)
    for (let i = 0; i < outLength; i++) {
      const srcPos = i * this.resampleRatio
      const srcIndex = Math.floor(srcPos)
      const frac = srcPos - srcIndex
      const a = channelData[Math.min(srcIndex, channelData.length - 1)]
      const b = channelData[Math.min(srcIndex + 1, channelData.length - 1)]
      const sample = Math.max(-1, Math.min(1, a + (b - a) * frac))
      pcm16[i] = sample < 0 ? sample * 0x8000 : sample * 0x7fff
    }

    this.port.postMessage(pcm16.buffer, [pcm16.buffer])
    return true
  }
}

registerProcessor('pcm-capture-processor', PcmCaptureProcessor)
