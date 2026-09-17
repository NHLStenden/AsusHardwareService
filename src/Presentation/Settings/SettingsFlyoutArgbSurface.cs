using System.Runtime.InteropServices;

namespace AsusHardwareService.Presentation.Settings;

/// <summary>
/// Owns the premultiplied-ARGB pixels used to compose translucent settings controls before the
/// frame is copied into the Win32 DIB. This keeps GDI from corrupting alpha on Acrylic surfaces.
/// </summary>
internal sealed class SettingsFlyoutArgbSurface
{
    private const int AntialiasSampleCount = 4;
    private readonly int[] _pixels;

    /// <summary>Initializes an ARGB surface with the supplied pixel dimensions.</summary>
    internal SettingsFlyoutArgbSurface(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        _pixels = new int[checked(width * height)];
    }

    internal int Width { get; }

    internal int Height { get; }

    /// <summary>Resets every pixel to one premultiplied ARGB value.</summary>
    internal void Clear(uint premultipliedArgb)
    {
        if (premultipliedArgb == 0)
        {
            Array.Clear(_pixels, 0, _pixels.Length);
            return;
        }

        Array.Fill(_pixels, unchecked((int)premultipliedArgb));
    }

    /// <summary>Copies this complete frame to a 32-bpp top-down DIB.</summary>
    internal void CopyTo(IntPtr bits)
    {
        if (bits != IntPtr.Zero)
        {
            Marshal.Copy(_pixels, 0, bits, _pixels.Length);
        }
    }

    /// <summary>Synchronizes this frame from a 32-bpp DIB after a rare direct-GDI fallback.</summary>
    internal void CopyFrom(IntPtr bits)
    {
        if (bits != IntPtr.Zero)
        {
            Marshal.Copy(bits, _pixels, 0, _pixels.Length);
        }
    }

    /// <summary>
    /// Source-over blends an antialiased rounded rectangle whose input color is straight ARGB.
    /// </summary>
    internal void DrawRoundedRect(
        int left,
        int top,
        int right,
        int bottom,
        int radius,
        uint argb)
    {
        if (right <= left || bottom <= top || (argb >> 24) == 0)
        {
            return;
        }

        var clippedLeft = Math.Max(0, left);
        var clippedTop = Math.Max(0, top);
        var clippedRight = Math.Min(Width, right);
        var clippedBottom = Math.Min(Height, bottom);
        for (var y = clippedTop; y < clippedBottom; y++)
        {
            var row = y * Width;
            for (var x = clippedLeft; x < clippedRight; x++)
            {
                var coverage = GetRoundedRectCoverage(x, y, left, top, right, bottom, radius);
                if (coverage == 0)
                {
                    continue;
                }

                var index = row + x;
                _pixels[index] = BlendStraightArgb(_pixels[index], argb, coverage);
            }
        }
    }

    /// <summary>
    /// Draws an antialiased rounded elevation stroke with a vertical ARGB gradient.
    /// </summary>
    internal void DrawRoundedRectStroke(
        int left,
        int top,
        int right,
        int bottom,
        int radius,
        int thickness,
        uint topArgb,
        uint bottomArgb)
    {
        if (right <= left || bottom <= top || thickness <= 0 ||
            (((topArgb | bottomArgb) >> 24) == 0))
        {
            return;
        }

        var clippedLeft = Math.Max(0, left);
        var clippedTop = Math.Max(0, top);
        var clippedRight = Math.Min(Width, right);
        var clippedBottom = Math.Min(Height, bottom);
        var innerLeft = left + thickness;
        var innerTop = top + thickness;
        var innerRight = right - thickness;
        var innerBottom = bottom - thickness;
        var innerRadius = Math.Max(0, radius - thickness);
        var gradientHeight = Math.Max(1, bottom - top - 1);

        for (var y = clippedTop; y < clippedBottom; y++)
        {
            var row = y * Width;
            var gradient = Math.Clamp((y - top) / (double)gradientHeight, 0.0, 1.0);
            var strokeArgb = LerpArgb(topArgb, bottomArgb, gradient);
            for (var x = clippedLeft; x < clippedRight; x++)
            {
                var outerCoverage = GetRoundedRectCoverage(x, y, left, top, right, bottom, radius);
                if (outerCoverage == 0)
                {
                    continue;
                }

                var innerCoverage = innerRight > innerLeft && innerBottom > innerTop
                    ? GetRoundedRectCoverage(
                        x,
                        y,
                        innerLeft,
                        innerTop,
                        innerRight,
                        innerBottom,
                        innerRadius)
                    : 0;
                var coverage = Math.Max(0, outerCoverage - innerCoverage);
                if (coverage == 0)
                {
                    continue;
                }

                var index = row + x;
                _pixels[index] = BlendStraightArgb(_pixels[index], strokeArgb, coverage);
            }
        }
    }

    /// <summary>
    /// Source-over blends a premultiplied ARGB bitmap into this surface at the supplied origin.
    /// </summary>
    internal void BlendPremultiplied(
        int[] sourcePixels,
        int sourceWidth,
        int sourceHeight,
        int destinationX,
        int destinationY)
    {
        ArgumentNullException.ThrowIfNull(sourcePixels);
        if (sourceWidth <= 0 || sourceHeight <= 0 ||
            sourcePixels.Length < checked(sourceWidth * sourceHeight))
        {
            return;
        }

        for (var sourceY = 0; sourceY < sourceHeight; sourceY++)
        {
            var targetY = destinationY + sourceY;
            if ((uint)targetY >= (uint)Height)
            {
                continue;
            }

            var sourceRow = sourceY * sourceWidth;
            var targetRow = targetY * Width;
            for (var sourceX = 0; sourceX < sourceWidth; sourceX++)
            {
                var targetX = destinationX + sourceX;
                if ((uint)targetX >= (uint)Width)
                {
                    continue;
                }

                var source = unchecked((uint)sourcePixels[sourceRow + sourceX]);
                var sourceAlpha = (int)((source >> 24) & 0xffu);
                if (sourceAlpha == 0)
                {
                    continue;
                }

                var targetIndex = targetRow + targetX;
                var destination = unchecked((uint)_pixels[targetIndex]);
                var inverseAlpha = 255 - sourceAlpha;
                var alpha = sourceAlpha +
                    (((int)((destination >> 24) & 0xffu) * inverseAlpha + 127) / 255);
                var red = (int)((source >> 16) & 0xffu) +
                    (((int)((destination >> 16) & 0xffu) * inverseAlpha + 127) / 255);
                var green = (int)((source >> 8) & 0xffu) +
                    (((int)((destination >> 8) & 0xffu) * inverseAlpha + 127) / 255);
                var blue = (int)(source & 0xffu) +
                    (((int)(destination & 0xffu) * inverseAlpha + 127) / 255);

                _pixels[targetIndex] = unchecked((int)(
                    ((uint)Math.Min(255, alpha) << 24) |
                    ((uint)Math.Min(255, red) << 16) |
                    ((uint)Math.Min(255, green) << 8) |
                    (uint)Math.Min(255, blue)));
            }
        }
    }

    private static int GetRoundedRectCoverage(
        int pixelX,
        int pixelY,
        int left,
        int top,
        int right,
        int bottom,
        int radius)
    {
        if (pixelX < left || pixelX >= right || pixelY < top || pixelY >= bottom)
        {
            return 0;
        }

        var effectiveRadius = Math.Max(0, Math.Min(radius, Math.Min(right - left, bottom - top) / 2));
        if (effectiveRadius == 0 ||
            (pixelX >= left + effectiveRadius && pixelX + 1 <= right - effectiveRadius) ||
            (pixelY >= top + effectiveRadius && pixelY + 1 <= bottom - effectiveRadius))
        {
            return 255;
        }

        var hits = 0;
        for (var sampleY = 0; sampleY < AntialiasSampleCount; sampleY++)
        {
            var y = pixelY + ((sampleY + 0.5) / AntialiasSampleCount);
            for (var sampleX = 0; sampleX < AntialiasSampleCount; sampleX++)
            {
                var x = pixelX + ((sampleX + 0.5) / AntialiasSampleCount);
                if (IsInsideRoundedRect(x, y, left, top, right, bottom, radius))
                {
                    hits++;
                }
            }
        }

        var totalSamples = AntialiasSampleCount * AntialiasSampleCount;
        return (hits * 255 + (totalSamples / 2)) / totalSamples;
    }

    private static bool IsInsideRoundedRect(
        double x,
        double y,
        int left,
        int top,
        int right,
        int bottom,
        int radius)
    {
        if (x < left || x >= right || y < top || y >= bottom)
        {
            return false;
        }

        var effectiveRadius = Math.Max(
            0.0,
            Math.Min(radius, Math.Min(right - left, bottom - top) / 2.0));
        if (effectiveRadius <= 0.0 ||
            (x >= left + effectiveRadius && x < right - effectiveRadius) ||
            (y >= top + effectiveRadius && y < bottom - effectiveRadius))
        {
            return true;
        }

        var centerX = x < left + effectiveRadius
            ? left + effectiveRadius
            : right - effectiveRadius;
        var centerY = y < top + effectiveRadius
            ? top + effectiveRadius
            : bottom - effectiveRadius;
        var deltaX = x - centerX;
        var deltaY = y - centerY;
        return (deltaX * deltaX) + (deltaY * deltaY) <= effectiveRadius * effectiveRadius;
    }

    private static int BlendStraightArgb(int destinationPixel, uint sourceArgb, int coverage)
    {
        var sourceAlpha = (int)((((sourceArgb >> 24) & 0xffu) * (uint)coverage + 127u) / 255u);
        if (sourceAlpha <= 0)
        {
            return destinationPixel;
        }

        var destination = unchecked((uint)destinationPixel);
        var inverseAlpha = 255 - sourceAlpha;
        var destinationAlpha = (int)((destination >> 24) & 0xffu);
        var destinationRed = (int)((destination >> 16) & 0xffu);
        var destinationGreen = (int)((destination >> 8) & 0xffu);
        var destinationBlue = (int)(destination & 0xffu);
        var sourceRed = (int)((sourceArgb >> 16) & 0xffu);
        var sourceGreen = (int)((sourceArgb >> 8) & 0xffu);
        var sourceBlue = (int)(sourceArgb & 0xffu);

        var alpha = sourceAlpha + ((destinationAlpha * inverseAlpha + 127) / 255);
        var red = ((sourceRed * sourceAlpha + 127) / 255) +
            ((destinationRed * inverseAlpha + 127) / 255);
        var green = ((sourceGreen * sourceAlpha + 127) / 255) +
            ((destinationGreen * inverseAlpha + 127) / 255);
        var blue = ((sourceBlue * sourceAlpha + 127) / 255) +
            ((destinationBlue * inverseAlpha + 127) / 255);

        return unchecked((int)(
            ((uint)Math.Min(255, alpha) << 24) |
            ((uint)Math.Min(255, red) << 16) |
            ((uint)Math.Min(255, green) << 8) |
            (uint)Math.Min(255, blue)));
    }

    /// <summary>Linearly interpolates two straight ARGB colors, including their alpha channels.</summary>
    internal static uint LerpArgb(uint fromArgb, uint toArgb, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);
        static byte Lerp(byte from, byte to, double t) =>
            (byte)Math.Round(from + ((to - from) * t));

        var alpha = Lerp((byte)(fromArgb >> 24), (byte)(toArgb >> 24), amount);
        var red = Lerp((byte)(fromArgb >> 16), (byte)(toArgb >> 16), amount);
        var green = Lerp((byte)(fromArgb >> 8), (byte)(toArgb >> 8), amount);
        var blue = Lerp((byte)fromArgb, (byte)toArgb, amount);
        return ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue;
    }
}
