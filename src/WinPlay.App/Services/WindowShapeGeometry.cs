// SPDX-License-Identifier: GPL-3.0-or-later
namespace WinPlay.App.Services;

internal readonly record struct WindowShapeGeometry(int Left, int Top, int Width, int Height, int Radius)
{
    internal int Right => checked(Left + Width);
    internal int Bottom => checked(Top + Height);

    internal static WindowShapeGeometry Create(int left, int top, int width, int height, uint dpi, bool capsule)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        int maximum = Math.Min(width, height) / 2;
        int radius = capsule ? maximum : Math.Min(maximum, (int)Math.Ceiling(18 * (dpi == 0 ? 96 : dpi) / 96.0));
        return new WindowShapeGeometry(left, top, width, height, radius);
    }
}
