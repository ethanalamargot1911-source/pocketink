using PocketInk.Core.Protocol;

namespace PocketInk.Tests;

public class SequenceTrackerTests
{
    [Fact]
    public void FirstPacket_IsInOrder()
    {
        var tracker = new SequenceTracker();
        Assert.Equal(SequenceResult.InOrder, tracker.Observe(1));
    }

    [Fact]
    public void IncreasingSequence_IsInOrder()
    {
        var tracker = new SequenceTracker();
        tracker.Observe(1);
        Assert.Equal(SequenceResult.InOrder, tracker.Observe(2));
        Assert.Equal(SequenceResult.InOrder, tracker.Observe(3));
    }

    [Fact]
    public void RepeatedSequence_IsDuplicate()
    {
        var tracker = new SequenceTracker();
        tracker.Observe(5);
        Assert.Equal(SequenceResult.Duplicate, tracker.Observe(5));
        Assert.Equal(1, tracker.Duplicates);
    }

    [Fact]
    public void LowerSequence_IsOutOfOrder()
    {
        var tracker = new SequenceTracker();
        tracker.Observe(10);
        Assert.Equal(SequenceResult.OutOfOrder, tracker.Observe(3));
        Assert.Equal(1, tracker.OutOfOrderCount);
    }

    [Fact]
    public void SkippedSequence_EstimatesLoss()
    {
        var tracker = new SequenceTracker();
        tracker.Observe(1);
        tracker.Observe(5);
        Assert.Equal(3, tracker.EstimatedLost);
    }

    [Fact]
    public void DoesNotDelayFreshInputForMissingPackets()
    {
        // Observing a large jump forward must still be classified in-order immediately,
        // not held back waiting for the skipped sequence numbers (spec #31).
        var tracker = new SequenceTracker();
        tracker.Observe(1);
        Assert.Equal(SequenceResult.InOrder, tracker.Observe(1000));
    }
}
