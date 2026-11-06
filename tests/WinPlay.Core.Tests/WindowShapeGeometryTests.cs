// SPDX-License-Identifier: GPL-3.0-or-later
using WinPlay.App.Services;
using Xunit;

namespace WinPlay.Core.Tests;

public class WindowShapeGeometryTests
{
    [Theory]
    [InlineData(96, 384, 220, 18)]
    [InlineData(144, 576, 330, 27)]
    [InlineData(192, 768, 440, 36)]
    public void WindowCornerRetainsEighteenLogicalPixels(int dpi, int width, int height, int expectedRadius)
    {
        var geometry = WindowShapeGeometry.Create(8, 0, width, height, (uint)dpi, capsule: false);
        Assert.Equal(expectedRadius, geometry.Radius);
        Assert.Equal(8 + width, geometry.Right);
        Assert.Equal(height, geometry.Bottom);
    }

    [Theory]
    [InlineData(420, 64, 32)]
    [InlineData(630, 96, 48)]
    [InlineData(840, 128, 64)]
    public void HudEndsUseHalfOfItsPhysicalHeight(int width, int height, int expectedRadius)
    {
        var geometry = WindowShapeGeometry.Create(8, 0, width, height, 96, capsule: true);
        Assert.Equal(expectedRadius, geometry.Radius);
    }

    [Fact]
    public void CornerNeverExceedsHalfOfASmallSurface()
    {
        Assert.Equal(5, WindowShapeGeometry.Create(0, 0, 40, 10, 192, capsule: false).Radius);
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowShapeGeometry.Create(0, 0, 0, 10, 96, capsule: false));
    }
}
