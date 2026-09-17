namespace PocketInk.Core.Protocol;

[Flags]
public enum InputPacketFlags : ushort
{
    None = 0,
    Primary = 1 << 0,
    BarrelButton = 1 << 1,
    Eraser = 1 << 2,
}
