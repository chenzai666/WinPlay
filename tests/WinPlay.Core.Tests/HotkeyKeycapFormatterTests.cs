using WinPlay.App.Services;
using Xunit;

namespace WinPlay.Core.Tests;

public class HotkeyKeycapFormatterTests
{
    [Theory]
    [InlineData("Ctrl+PgUp", new[] { "Ctrl", "PgUp" })]
    [InlineData(" Ctrl - Shift + F1 ", new[] { "Ctrl", "Shift", "F1" })]
    [InlineData("VolumeUp", new[] { "VolumeUp" })]
    [InlineData("Ctrl++PgDn", new[] { "Ctrl", "PgDn" })]
    public void Split_UsesTheSameSeparatorsAsChordParsing(string text, string[] expected) =>
        Assert.Equal(expected, HotkeyKeycapFormatter.Split(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" + - ")]
    public void Split_EmptyInputHasNoKeys(string? text) =>
        Assert.Empty(HotkeyKeycapFormatter.Split(text));
}
