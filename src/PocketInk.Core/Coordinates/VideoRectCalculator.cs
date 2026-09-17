namespace PocketInk.Core.Coordinates;

/// <summary>
/// Computes the actual rendered content rectangle of a &lt;video&gt; element
/// using object-fit: contain semantics, so touches on letterboxed video map
/// to the correct source pixel rather than the full DOM element bounds
/// (spec #65, #66).
/// </summary>
public static class VideoRectCalculator
{
    public static RectD ComputeContainRect(double videoWidth, double videoHeight, double elementWidth, double elementHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0 || elementWidth <= 0 || elementHeight <= 0)
        {
            return new RectD(0, 0, Math.Max(elementWidth, 0), Math.Max(elementHeight, 0));
        }

        var videoAspect = videoWidth / videoHeight;
        var elementAspect = elementWidth / elementHeight;

        if (videoAspect > elementAspect)
        {
            // Video is relatively wider than the element: full width, letterboxed top/bottom.
            var contentHeight = elementWidth / videoAspect;
            var offsetY = (elementHeight - contentHeight) / 2.0;
            return new RectD(0, offsetY, elementWidth, contentHeight);
        }
        else
        {
            // Video is relatively taller than the element: full height, letterboxed left/right (pillarbox).
            var contentWidth = elementHeight * videoAspect;
            var offsetX = (elementWidth - contentWidth) / 2.0;
            return new RectD(offsetX, 0, contentWidth, elementHeight);
        }
    }

    /// <summary>Converts a touch point in element-local coordinates to normalized [0,1] video-content coordinates, or null if the touch fell on a letterbox/pillarbox bar.</summary>
    public static (double X, double Y)? PointToNormalized(RectD contentRect, double pointX, double pointY)
    {
        if (contentRect.Width <= 0 || contentRect.Height <= 0 || !contentRect.Contains(pointX, pointY))
        {
            return null;
        }

        var x = (pointX - contentRect.Left) / contentRect.Width;
        var y = (pointY - contentRect.Top) / contentRect.Height;
        return (Math.Clamp(x, 0.0, 1.0), Math.Clamp(y, 0.0, 1.0));
    }
}
