// SPDX-License-Identifier: GPL-3.0-or-later
using WinPlay.App.Services;
using Xunit;

namespace WinPlay.Core.Tests;

public class AcrylicMaterialTests
{
    [Theory]
    [InlineData(0, 1f)]
    [InlineData(70, 1f)]
    [InlineData(85, 0.5f)]
    [InlineData(90, 1f / 3f)]
    [InlineData(95, 1f / 6f)]
    [InlineData(100, 0f)]
    public void PopupMaterialFadesAboveSeventyAndDisappearsAtOneHundred(int value, float expected)
    {
        Assert.Equal(expected, AcrylicMaterial.PopupBackdropOpacity(value), 5);
    }

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

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(90)]
    [InlineData(100)]
    public void DarkSettingsKeepReadableLuminosityWithoutChangingPopupMapping(int setting)
    {
        var popup = AcrylicMaterial.FromTransparency(setting);
        var lightSettings = AcrylicMaterial.ForSettings(setting, dark: false);
        var darkSettings = AcrylicMaterial.ForSettings(setting, dark: true);

        Assert.Equal(popup, lightSettings);
        Assert.Equal(popup.TintOpacity, darkSettings.TintOpacity);
        Assert.InRange(darkSettings.LuminosityOpacity, 0.75f, 0.9f);
        Assert.InRange(popup.LuminosityOpacity, 0f, 0.2f);
    }
}
