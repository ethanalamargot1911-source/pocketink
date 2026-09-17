using System.Buffers.Binary;

namespace PocketInk.Core.Protocol;

/// <summary>
/// INPUT_PACKET_V1 binary layout (all multi-byte fields little-endian).
/// Floats are transmitted as explicit IEEE-754 bit patterns rather than
/// relying on native struct layout, so the JavaScript and C# encoders are
/// guaranteed to agree (spec #80, #81). See PROTOCOL.md for the canonical
/// description and golden test vector.
///
/// Offset  Size  Field
/// 0       2     Magic        (0x504B)
/// 2       1     Version      (1)
/// 3       1     Phase        (PointerPhase)
/// 4       4     Sequence     (uint32)
/// 8       4     X            (float32, 0.0 - 1.0)
/// 12      4     Y            (float32, 0.0 - 1.0)
/// 16      2     Pressure     (uint16, 0 - 1024)
/// 18      2     Flags        (InputPacketFlags)
/// 20      8     Timestamp    (uint64, client epoch milliseconds)
/// Total: 28 bytes.
/// </summary>
public readonly struct InputPacket
{
    public const ushort Magic = 0x504B;
    public const byte CurrentVersion = 1;
    public const int WireSize = 28;

    /// <summary>Small tolerance so a pointer exactly on the drawing surface edge is not rejected.</summary>
    private const float CoordinateTolerance = 0.001f;

    public byte Version { get; }
    public PointerPhase Phase { get; }
    public uint Sequence { get; }
    public float X { get; }
    public float Y { get; }
    public ushort Pressure { get; }
    public InputPacketFlags Flags { get; }
    public ulong TimestampMs { get; }

    public InputPacket(byte version, PointerPhase phase, uint sequence, float x, float y, ushort pressure, InputPacketFlags flags, ulong timestampMs)
    {
        Version = version;
        Phase = phase;
        Sequence = sequence;
        X = x;
        Y = y;
        Pressure = pressure;
        Flags = flags;
        TimestampMs = timestampMs;
    }

    public void Encode(Span<byte> destination)
    {
        if (destination.Length < WireSize)
        {
            throw new ArgumentException($"Destination buffer must be at least {WireSize} bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination[0..2], Magic);
        destination[2] = Version;
        destination[3] = (byte)Phase;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..8], Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..12], BitConverter.SingleToUInt32Bits(X));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..16], BitConverter.SingleToUInt32Bits(Y));
        BinaryPrimitives.WriteUInt16LittleEndian(destination[16..18], Pressure);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[18..20], (ushort)Flags);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[20..28], TimestampMs);
    }

    public byte[] Encode()
    {
        var buffer = new byte[WireSize];
        Encode(buffer);
        return buffer;
    }

    /// <summary>
    /// Attempts to decode and validate a wire packet. Never trusts the browser blindly (spec #83):
    /// rejects bad magic/version, unknown phases, NaN/Infinity, out-of-range coordinates and pressure.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> buffer, out InputPacket packet, out string? error)
    {
        packet = default;
        error = null;

        if (buffer.Length != WireSize)
        {
            error = $"Expected {WireSize} bytes, got {buffer.Length}.";
            return false;
        }

        var magic = BinaryPrimitives.ReadUInt16LittleEndian(buffer[0..2]);
        if (magic != Magic)
        {
            error = $"Bad magic 0x{magic:X4}.";
            return false;
        }

        var version = buffer[2];
        if (version != CurrentVersion)
        {
            error = $"Unsupported protocol version {version}.";
            return false;
        }

        var rawPhase = buffer[3];
        if (rawPhase > (byte)PointerPhase.Cancel)
        {
            error = $"Unknown phase {rawPhase}.";
            return false;
        }

        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..8]);
        var x = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..12]));
        var y = BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(buffer[12..16]));
        var pressure = BinaryPrimitives.ReadUInt16LittleEndian(buffer[16..18]);
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(buffer[18..20]);
        var timestamp = BinaryPrimitives.ReadUInt64LittleEndian(buffer[20..28]);

        if (float.IsNaN(x) || float.IsInfinity(x) || x < -CoordinateTolerance || x > 1f + CoordinateTolerance)
        {
            error = $"Invalid x coordinate {x}.";
            return false;
        }

        if (float.IsNaN(y) || float.IsInfinity(y) || y < -CoordinateTolerance || y > 1f + CoordinateTolerance)
        {
            error = $"Invalid y coordinate {y}.";
            return false;
        }

        if (pressure > 1024)
        {
            error = $"Invalid pressure {pressure}.";
            return false;
        }

        packet = new InputPacket(version, (PointerPhase)rawPhase, sequence, x, y, pressure, (InputPacketFlags)flags, timestamp);
        return true;
    }
}
