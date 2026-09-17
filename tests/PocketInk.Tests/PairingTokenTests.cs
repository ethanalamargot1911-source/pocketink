using PocketInk.Core.Models;

namespace PocketInk.Tests;

public class PairingTokenTests
{
    [Fact]
    public void ValidToken_CanBeConsumedOnce()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var token = PairingToken.CreateNew(TimeSpan.FromMinutes(2), time);

        Assert.True(token.TryConsume(token.Value, time.GetUtcNow()));
    }

    [Fact]
    public void ReusedToken_IsRejected()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var token = PairingToken.CreateNew(TimeSpan.FromMinutes(2), time);

        Assert.True(token.TryConsume(token.Value, time.GetUtcNow()));
        Assert.False(token.TryConsume(token.Value, time.GetUtcNow()));
    }

    [Fact]
    public void ExpiredToken_IsRejected()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var token = PairingToken.CreateNew(TimeSpan.FromMinutes(2), time);

        time.Advance(TimeSpan.FromMinutes(3));
        Assert.False(token.TryConsume(token.Value, time.GetUtcNow()));
    }

    [Fact]
    public void IncorrectToken_IsRejected()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var token = PairingToken.CreateNew(TimeSpan.FromMinutes(2), time);

        Assert.False(token.TryConsume("not-the-real-token", time.GetUtcNow()));
    }

    [Fact]
    public void TokenValue_HasAtLeast128BitsOfEntropy()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var token = PairingToken.CreateNew(TimeSpan.FromMinutes(2), time);

        // 128 bits = 16 bytes = 32 hex characters.
        Assert.Equal(32, token.Value.Length);
    }

    [Fact]
    public void TwoTokens_AreNotIdentical()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var a = PairingToken.CreateNew(TimeSpan.FromMinutes(2), time);
        var b = PairingToken.CreateNew(TimeSpan.FromMinutes(2), time);

        Assert.NotEqual(a.Value, b.Value);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
