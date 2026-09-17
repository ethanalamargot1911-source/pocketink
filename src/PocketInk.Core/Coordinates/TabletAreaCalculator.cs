namespace PocketInk.Core.Coordinates;

/// <summary>
/// Computes the largest rectangle that fits inside the available phone
/// drawing area while matching the target monitor's aspect ratio (spec #108,
/// #109). Touches outside the returned rectangle fall in inactive padding
/// and must not be mapped to the monitor, to avoid distorting the drawing.
/// </summary>
public static class TabletAreaCalculator
{
    public static RectD ComputeMatchedAspectArea(double availableWidth, double availableHeight, double targetAspectRatio)
    {
        if (availableWidth <= 0 || availableHeight <= 0 || targetAspectRatio <= 0)
        {
            return new RectD(0, 0, Math.Max(availableWidth, 0), Math.Max(availableHeight, 0));
        }

        var candidateWidth = availableHeight * targetAspectRatio;
        if (candidateWidth <= availableWidth)
        {
            var offsetX = (availableWidth - candidateWidth) / 2.0;
            return new RectD(offsetX, 0, candidateWidth, availableHeight);
        }

        var candidateHeight = availableWidth / targetAspectRatio;
        var offsetY = (availableHeight - candidateHeight) / 2.0;
        return new RectD(0, offsetY, availableWidth, candidateHeight);
    }

    /// <summary>Converts a point in the raw available area to normalized [0,1] coordinates within the matched-aspect rect, or null if outside it (inactive padding).</summary>
    public static (double X, double Y)? PointToNormalized(RectD matchedArea, double pointX, double pointY)
    {
        if (matchedArea.Width <= 0 || matchedArea.Height <= 0 || !matchedArea.Contains(pointX, pointY))
        {
            return null;
        }

        var x = (pointX - matchedArea.Left) / matchedArea.Width;
        var y = (pointY - matchedArea.Top) / matchedArea.Height;
        return (Math.Clamp(x, 0.0, 1.0), Math.Clamp(y, 0.0, 1.0));
    }
}
