// SPDX-License-Identifier: GPL-3.0-or-later
namespace WinPlay.App.Services;

/// <summary>Maps the user's clarity setting to the acrylic tint and luminosity layers.</summary>
internal readonly record struct AcrylicMaterial(float TintOpacity, float LuminosityOpacity)
{
    public static AcrylicMaterial FromTransparency(int transparency)
    {
        float opacity = 1f - Math.Clamp(transparency, 0, 100) / 100f;
        return new AcrylicMaterial(opacity, 0.2f * opacity);
    }
}
