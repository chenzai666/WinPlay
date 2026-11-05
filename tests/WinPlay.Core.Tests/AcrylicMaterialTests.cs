// SPDX-License-Identifier: GPL-3.0-or-later
using WinPlay.App.Services;
using Xunit;

namespace WinPlay.Core.Tests;

public class AcrylicMaterialTests
{
    [Theory]
    [InlineData(-1, 1.0f, 0.2f)]
    [InlineData(0, 1.0f, 0.2f)]
    [InlineData(50, 0.5f, 0.1f)]
    [InlineData(90, 0.1f, 0.02f)]
    [InlineData(100, 0f, 0f)]
    [InlineData(101, 0f, 0f)]
    public void TransparencyMapsToTintAndLuminosity(int setting, float expectedTint, float expectedLuminosity)
    {
        AcrylicMaterial material = AcrylicMaterial.FromTransparency(setting);

        Assert.InRange(material.TintOpacity, expectedTint - 0.00001f, expectedTint + 0.00001f);
        Assert.InRange(material.LuminosityOpacity, expectedLuminosity - 0.00001f, expectedLuminosity + 0.00001f);
    }

    [Fact]
    public void IncreasingTransparencyNeverMakesEitherLayerMoreOpaque()
    {
        AcrylicMaterial previous = AcrylicMaterial.FromTransparency(0);
        for (int setting = 1; setting <= 100; setting++)
        {
            AcrylicMaterial current = AcrylicMaterial.FromTransparency(setting);
            Assert.True(current.TintOpacity <= previous.TintOpacity);
            Assert.True(current.LuminosityOpacity <= previous.LuminosityOpacity);
            previous = current;
        }
    }
}
