namespace PocketInk.Core.Coordinates;

/// <summary>Simple double-precision rectangle used for tablet-area and video letterbox math.</summary>
public readonly struct RectD
{
    public double Left { get; }
    public double Top { get; }
    public double Width { get; }
    public double Height { get; }

    public RectD(double left, double top, double width, double height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}
