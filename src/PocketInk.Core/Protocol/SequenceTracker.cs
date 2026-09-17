namespace PocketInk.Core.Protocol;

public enum SequenceResult
{
    InOrder,
    Duplicate,
    OutOfOrder,
}

/// <summary>
/// Tracks a monotonically increasing per-session sequence number so the host
/// can classify received/duplicate/out-of-order/estimated-lost packets
/// without ever delaying fresh input while waiting for a missing one (spec #31).
/// </summary>
public sealed class SequenceTracker
{
    private bool _hasSeen;
    private uint _highestSeen;

    public long Received { get; private set; }
    public long Duplicates { get; private set; }
    public long OutOfOrderCount { get; private set; }
    public long EstimatedLost { get; private set; }

    public SequenceResult Observe(uint sequence)
    {
        Received++;

        if (!_hasSeen)
        {
            _hasSeen = true;
            _highestSeen = sequence;
            return SequenceResult.InOrder;
        }

        if (sequence == _highestSeen)
        {
            Duplicates++;
            return SequenceResult.Duplicate;
        }

        // Unsigned wraparound-aware "is newer" check.
        var delta = unchecked(sequence - _highestSeen);
        var isNewer = delta != 0 && delta < 0x8000_0000;

        if (!isNewer)
        {
            OutOfOrderCount++;
            return SequenceResult.OutOfOrder;
        }

        if (delta > 1)
        {
            EstimatedLost += delta - 1;
        }

        _highestSeen = sequence;
        return SequenceResult.InOrder;
    }
}
