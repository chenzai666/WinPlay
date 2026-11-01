// SPDX-License-Identifier: GPL-3.0-or-later
namespace WinPlay.Core;

/// <summary>
/// Diagnostic files under %TEMP%. Off unless WINPLAY_LOG=1 (or WINPLAY_STARTUP_LOG=1).
/// </summary>
public static class FileLog
{
    public static bool Enabled { get; } =
        Environment.GetEnvironmentVariable("WINPLAY_LOG") == "1"
        || Environment.GetEnvironmentVariable("WINPLAY_STARTUP_LOG") == "1";

    public static void AppendTemp(string fileName, string line)
    {
        if (!Enabled) return;
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), fileName),
                line.EndsWith('\n') ? line : line + Environment.NewLine);
        }
        catch (Exception) { }
    }
}
