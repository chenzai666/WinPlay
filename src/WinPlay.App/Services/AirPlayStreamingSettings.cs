// SPDX-License-Identifier: GPL-3.0-or-later
namespace WinPlay.App.Services;

/// <summary>
/// TuneBlade-style AirPlay buffer. Lower buffer = less lip-sync lag, more risk of dropouts.
/// Applied the next time a speaker is connected.
/// </summary>
public static class AirPlayStreamingSettings
{
    public enum Mode
    {
        RealTime = 300,
        Low = 700,
        Normal = 2000,
        Buffered = 3000,
    }

    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinPlay", "streaming-buffer.txt");

    public static Mode Current { get; private set; } = Mode.RealTime;

    public static int LatencyFrames =>
        WinPlay.Core.Raop.RaopSession.FramesForBufferMilliseconds((int)Current);

    static AirPlayStreamingSettings()
    {
        try
        {
            if (int.TryParse(File.ReadAllText(Path).Trim(), out int ms)
                && Enum.IsDefined(typeof(Mode), ms))
                Current = (Mode)ms;
        }
        catch (Exception) { }
    }

    public static void Set(Mode mode)
    {
        Current = mode;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, ((int)mode).ToString());
        }
        catch (Exception) { }
    }
}
