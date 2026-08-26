namespace FastTTSR.Api.Contracts;

/// <summary>
/// Tells the frontend which task types this server instance was started with (SERVER_MODE).
/// </summary>
public sealed record ServerInfoResponse(bool TtsEnabled, bool AsrEnabled);
