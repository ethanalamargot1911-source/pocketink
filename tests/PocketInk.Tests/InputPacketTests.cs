using PocketInk.Core.Protocol;

namespace PocketInk.Tests;

public class InputPacketTests
{
    /// <summary>
    /// Golden test vector (spec #93). sequence=42, phase=move, x=0.5, y=0.25,
    /// pressure=512, flags=0, timestamp=0. Browser and host encoders must
    /// produce these exact 28 bytes - see PROTOCOL.md.
    /// </summary>
    private static readonly byte[] GoldenVectorBytes =
    {
        0x4B, 0x50,             // magic 0x504B (LE)
        0x01,                   // version
        0x00,                   // phase = Move
        0x2A, 0x00, 0x00, 0x00, // sequence = 42
        0x00, 0x00, 0x00, 0x3F, // x = 0.5f
        0x00, 0x00, 0x80, 0x3E, // y = 0.25f
        0x00, 0x02,             // pressure = 512
        0x00, 0x00,             // flags = 0
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // timestamp = 0
    };

    [Fact]
    public void Encode_GoldenVector_MatchesExactBytes()
    {
        var packet = new InputPacket(InputPacket.CurrentVersion, PointerPhase.Move, 42, 0.5f, 0.25f, 512, InputPacketFlags.None, 0);
        Assert.Equal(GoldenVectorBytes, packet.Encode());
    }

    [Fact]
    public void Decode_GoldenVector_ProducesExpectedFields()
    {
        var ok = InputPacket.TryDecode(GoldenVectorBytes, out var packet, out var error);
        Assert.True(ok, error);
        Assert.Equal(42u, packet.Sequence);
        Assert.Equal(PointerPhase.Move, packet.Phase);
        Assert.Equal(0.5f, packet.X);
        Assert.Equal(0.25f, packet.Y);
        Assert.Equal((ushort)512, packet.Pressure);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new InputPacket(InputPacket.CurrentVersion, PointerPhase.Down, 7, 0.1234f, 0.9876f, 900, InputPacketFlags.Primary, 1_700_000_000_000);
        var bytes = original.Encode();
        Assert.True(InputPacket.TryDecode(bytes, out var decoded, out _));

        Assert.Equal(original.Sequence, decoded.Sequence);
        Assert.Equal(original.Phase, decoded.Phase);
        Assert.Equal(original.X, decoded.X);
        Assert.Equal(original.Y, decoded.Y);
        Assert.Equal(original.Pressure, decoded.Pressure);
        Assert.Equal(original.Flags, decoded.Flags);
        Assert.Equal(original.TimestampMs, decoded.TimestampMs);
    }

    [Fact]
    public void Decode_WrongLength_IsRejected()
    {
        Assert.False(InputPacket.TryDecode(new byte[10], out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Decode_BadMagic_IsRejected()
    {
        var bytes = (byte[])GoldenVectorBytes.Clone();
        bytes[0] = 0xFF;
        Assert.False(InputPacket.TryDecode(bytes, out _, out var error));
        Assert.Contains("magic", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_UnsupportedVersion_IsRejected()
    {
        var bytes = (byte[])GoldenVectorBytes.Clone();
        bytes[2] = 99;
        Assert.False(InputPacket.TryDecode(bytes, out _, out var error));
        Assert.Contains("version", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_UnknownPhase_IsRejected()
    {
        var bytes = (byte[])GoldenVectorBytes.Clone();
        bytes[3] = 250;
        Assert.False(InputPacket.TryDecode(bytes, out _, out var error));
        Assert.Contains("phase", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_NaNCoordinate_IsRejected()
    {
        var packet = new InputPacket(InputPacket.CurrentVersion, PointerPhase.Move, 1, float.NaN, 0.5f, 0, InputPacketFlags.None, 0);
        var bytes = packet.Encode();
        Assert.False(InputPacket.TryDecode(bytes, out _, out var error));
        Assert.Contains("x coordinate", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_InfinityCoordinate_IsRejected()
    {
        var packet = new InputPacket(InputPacket.CurrentVersion, PointerPhase.Move, 1, 0.1f, float.PositiveInfinity, 0, InputPacketFlags.None, 0);
        var bytes = packet.Encode();
        Assert.False(InputPacket.TryDecode(bytes, out _, out var error));
        Assert.Contains("y coordinate", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_OutOfRangeCoordinate_IsRejected()
    {
        var packet = new InputPacket(InputPacket.CurrentVersion, PointerPhase.Move, 1, 50f, 0.5f, 0, InputPacketFlags.None, 0);
        var bytes = packet.Encode();
        Assert.False(InputPacket.TryDecode(bytes, out _, out var error));
        Assert.Contains("x coordinate", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_ImpossiblePressure_IsRejected()
    {
        var bytes = (byte[])GoldenVectorBytes.Clone();
        bytes[16] = 0xFF;
        bytes[17] = 0xFF; // pressure = 65535
        Assert.False(InputPacket.TryDecode(bytes, out _, out var error));
        Assert.Contains("pressure", error, StringComparison.OrdinalIgnoreCase);
    }
}
