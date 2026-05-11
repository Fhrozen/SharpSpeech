namespace FastTTSR.Api.Models;

public sealed record SynthesisResult(byte[] AudioBytes, string ContentType, string FileName);
