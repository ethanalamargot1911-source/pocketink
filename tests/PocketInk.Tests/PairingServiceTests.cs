using PocketInk.Core.Protocol;
using PocketInk.Host.Security;

namespace PocketInk.Tests;

public class PairingServiceTests
{
    private static string ExtractToken(string pairingUrl) => pairingUrl.Split("token=")[1];

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    [Fact]
    public void ValidToken_IssuesSessionThatValidatesAsPaired()
    {
        var service = new PairingService(TimeProvider.System);
        service.Initialize("http://192.168.1.50:17462");
        var token = ExtractToken(service.GetPairingUrl());

        var session = service.TryPair(token);

        Assert.NotNull(session);
        Assert.True(service.IsSessionValid(session));
        Assert.True(service.IsPaired);
    }

    [Fact]
    public void WrongToken_IsRejected()
    {
        var service = new PairingService(TimeProvider.System);
        service.Initialize("http://192.168.1.50:17462");

        var session = service.TryPair("not-the-real-token");

        Assert.Null(session);
        Assert.False(service.IsPaired);
    }

    [Fact]
    public void TokenIsSingleUse()
    {
        var service = new PairingService(TimeProvider.System);
        service.Initialize("http://192.168.1.50:17462");
        var token = ExtractToken(service.GetPairingUrl());

        var first = service.TryPair(token);
        var second = service.TryPair(token);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void ForgetPairedDevice_RevokesSessionsAndRotatesToken()
    {
        var service = new PairingService(TimeProvider.System);
        service.Initialize("http://192.168.1.50:17462");
        var oldToken = ExtractToken(service.GetPairingUrl());
        var session = service.TryPair(oldToken)!;

        service.ForgetPairedDevice();

        Assert.False(service.IsSessionValid(session));
        Assert.False(service.IsPaired);
        Assert.NotEqual(oldToken, ExtractToken(service.GetPairingUrl()));
    }

    [Fact]
    public void RefreshPairingToken_InvalidatesPreviousTokenButKeepsExistingSessions()
    {
        var service = new PairingService(TimeProvider.System);
        service.Initialize("http://192.168.1.50:17462");
        var oldToken = ExtractToken(service.GetPairingUrl());
        var session = service.TryPair(oldToken)!;

        service.RefreshPairingToken();

        Assert.True(service.IsSessionValid(session));
        Assert.Null(service.TryPair(oldToken));
    }

    [Fact]
    public void RefreshIfExpired_TokenNotYetExpired_ReturnsFalseAndKeepsToken()
    {
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var service = new PairingService(time);
        service.Initialize("http://192.168.1.50:17462");
        var token = ExtractToken(service.GetPairingUrl());

        var refreshed = service.RefreshIfExpired();

        Assert.False(refreshed);
        Assert.Equal(token, ExtractToken(service.GetPairingUrl()));
    }

    [Fact]
    public void RefreshIfExpired_TokenExpired_RotatesToken()
    {
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var service = new PairingService(time);
        service.Initialize("http://192.168.1.50:17462");
        var oldToken = ExtractToken(service.GetPairingUrl());
        time.Advance(ProtocolConstants.PairingTokenLifetime + TimeSpan.FromSeconds(1));

        var refreshed = service.RefreshIfExpired();

        Assert.True(refreshed);
        Assert.NotEqual(oldToken, ExtractToken(service.GetPairingUrl()));
    }

    [Fact]
    public void GeneratePairingQrPng_ProducesValidPngBytes()
    {
        var service = new PairingService(TimeProvider.System);
        service.Initialize("http://192.168.1.50:17462");

        var png = service.GeneratePairingQrPng();

        Assert.True(png.Length > 8);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
    }
}
