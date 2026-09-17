using Microsoft.Extensions.Logging;
using PocketInk.Core.Protocol;
using PocketInk.Core.Safety;

namespace PocketInk.Host.Input;

/// <summary>
/// Polls at a short interval and force-releases the synthetic pen contact if
/// no valid input has refreshed it within <see cref="ProtocolConstants.PenWatchdogTimeout"/>.
/// This is the last line of defense against a stuck pen (spec #36, #152) -
/// it must keep working even if the browser, WebSocket, or app crashes
/// without sending a clean release.
/// </summary>
public sealed class PenWatchdog : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly SyntheticPenService _penService;
    private readonly ILogger<PenWatchdog> _logger;
    private readonly TimeSpan _timeout;
    private readonly System.Threading.Timer _timer;
    private long _lastInputTicks;
    private volatile bool _armed;

    public PenWatchdog(SyntheticPenService penService, ILogger<PenWatchdog> logger, TimeSpan? timeout = null)
    {
        _penService = penService;
        _logger = logger;
        _timeout = timeout ?? ProtocolConstants.PenWatchdogTimeout;
        _timer = new System.Threading.Timer(OnTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Call whenever a valid input packet is processed, whether or not it changed contact state.</summary>
    public void NotifyInputReceived()
    {
        Interlocked.Exchange(ref _lastInputTicks, Environment.TickCount64);
    }

    public void Arm()
    {
        NotifyInputReceived();
        _armed = true;
        _timer.Change(PollInterval, PollInterval);
    }

    public void Disarm()
    {
        _armed = false;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void OnTick(object? state)
    {
        if (!_armed)
        {
            return;
        }

        var sinceLastInput = TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastInputTicks));
        if (PenWatchdogLogic.ShouldForceRelease(_penService.IsContactActive, sinceLastInput, _timeout))
        {
            _logger.LogWarning("Pen watchdog: no input for {ElapsedMs}ms while contact active - forcing release.", sinceLastInput.TotalMilliseconds);
            _penService.Cancel();
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
