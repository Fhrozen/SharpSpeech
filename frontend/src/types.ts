export interface TtsModel {
  name: string
  displayName: string
  description: string
  supportedLanguages: string[]
  speakers: string[]
}

export interface SynthesisRequest {
  model: string
  input: string
  speed: number
  speaker: string
  language: string
  response_format: string
  voice: string
  quality?: number
}

export interface SynthesisMetrics {
  processingTime: string
  charsPerSecond: string
  rtf: string
  audioDuration: string
  characterCount: string
}

export interface TextPreset {
  id: string
  label: string
  text: string
}

export type StatusType = 'loading' | 'success' | 'error' | ''
