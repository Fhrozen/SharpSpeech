namespace FastTTSR.Worker.Services;

/// <summary>
/// Monitors worker idle time and triggers shutdown callback after timeout
/// </summary>
public sealed class IdleMonitor : IDisposable
{
    private readonly TimeSpan _idleTimeout;
    private readonly Action _onIdleTimeout;
    private readonly Timer _timer;
    private DateTime _lastActivityTime;
    private readonly object _lock = new();

    public IdleMonitor(TimeSpan idleTimeout, Action onIdleTimeout)
    {
        _idleTimeout = idleTimeout;
        _onIdleTimeout = onIdleTimeout;
        _lastActivityTime = DateTime.UtcNow;
        
        // Check every 5 seconds
        _timer = new Timer(CheckIdleTimeout, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Record activity to reset idle timer
    /// </summary>
    public void RecordActivity()
    {
        lock (_lock)
        {
            _lastActivityTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Get current idle duration
    /// </summary>
    public TimeSpan GetIdleDuration()
    {
        lock (_lock)
        {
            return DateTime.UtcNow - _lastActivityTime;
        }
    }

    private void CheckIdleTimeout(object? state)
    {
        var idleDuration = GetIdleDuration();
        
        if (idleDuration >= _idleTimeout)
        {
            _onIdleTimeout();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
