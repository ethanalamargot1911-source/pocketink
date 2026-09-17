using PocketInk.Core.Coordinates;
using PocketInk.Core.Models;
using PocketInk.Core.Protocol;
using PocketInk.Host.Services;

namespace PocketInk.Host.Input;

/// <summary>
/// Per-connection input pipeline: sequence filtering, smoothing, pressure
/// simulation, and coordinate mapping, feeding the shared <see cref="SyntheticPenService"/>.
/// A fresh instance is created per WebSocket session so sequence numbers and
/// smoothing state never leak between connections (spec #31, #41).
/// </summary>
public sealed class PenInputPipeline
{
    private readonly HostServices _services;
    private readonly SequenceTracker _sequenceTracker = new();
    private readonly PointSmoother _pointSmoother;
    private readonly VelocityPressureSimulator _velocitySimulator = new();
    private double? _lastRawX;
    private double? _lastRawY;
    private ulong? _lastTimestampMs;

    /// <summary>Real (non-fake) per-connection sequence/loss counters, surfaced to the UI.</summary>
    public SequenceTracker Metrics => _sequenceTracker;

    public PenInputPipeline(HostServices services)
    {
        _services = services;
        _pointSmoother = new PointSmoother(services.Settings.Smoothing);
    }

    public void OnConnected() => _services.Watchdog.Arm();

    public void OnDisconnected()
    {
        _services.PenService.Cancel();
        _services.Watchdog.Disarm();
        _services.Hotkeys.ReleaseAllModifiers();
    }

    public void ProcessPacket(InputPacket packet)
    {
        if (_sequenceTracker.Observe(packet.Sequence) != SequenceResult.InOrder)
        {
            return;
        }

        _services.Watchdog.NotifyInputReceived();

        if (packet.Phase == PointerPhase.Down)
        {
            _pointSmoother.Reset();
            _velocitySimulator.Reset();
            _lastRawX = null;
            _lastRawY = null;
            _lastTimestampMs = null;
        }

        var (smoothX, smoothY) = _pointSmoother.Smooth(packet.X, packet.Y);
        var monitor = _services.Monitors.GetByDeviceId(_services.Settings.SelectedMonitorDeviceId)
            ?? _services.Monitors.GetPrimaryOrFirst();
        var (screenX, screenY) = CoordinateMapper.NormalizedToScreen(monitor, smoothX, smoothY);
        var pressure = ComputePressure(packet);

        switch (packet.Phase)
        {
            case PointerPhase.Down:
                _services.PenService.PenDown(screenX, screenY, pressure);
                break;
            case PointerPhase.Move:
            case PointerPhase.MoveContact:
                _services.PenService.PenMove(screenX, screenY, pressure);
                break;
            case PointerPhase.Up:
                _services.PenService.PenUp(screenX, screenY);
                break;
            case PointerPhase.Cancel:
                _services.PenService.Cancel();
                break;
        }

        _lastRawX = packet.X;
        _lastRawY = packet.Y;
        _lastTimestampMs = packet.TimestampMs;
    }

    private ushort ComputePressure(InputPacket packet)
    {
        switch (_services.Settings.PressureMode)
        {
            case PressureMode.Constant:
                return PressureConverter.FromFraction(_services.Settings.ConstantPressureFraction);
            case PressureMode.VelocitySimulated:
                double speed = 0.0;
                if (_lastRawX is not null && _lastRawY is not null && _lastTimestampMs is not null)
                {
                    speed = PointerSpeedCalculator.ComputeNormalizedSpeed(
                        _lastRawX.Value, _lastRawY.Value, _lastTimestampMs.Value,
                        packet.X, packet.Y, packet.TimestampMs);
                }
                return _velocitySimulator.ComputePressure(speed);
            default:
                return packet.Pressure;
        }
    }
}
