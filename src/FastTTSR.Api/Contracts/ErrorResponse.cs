namespace FastTTSR.Api.Contracts;

/// <summary>
/// Error response returned when a request fails
/// </summary>
/// <param name="Error">Error code</param>
/// <param name="Message">Human-readable error message</param>
public sealed record ErrorResponse(string Error, string Message);
