// SPDX-License-Identifier: GPL-3.0-or-later
namespace WinPlay.Core.Raop;

// Audio positions and clock times must describe the same instant, independent
// of when the sync worker happens to run. Each packet holds 352 stereo frames.
internal readonly record struct AudioTimeline(uint StartRtp, ulong StartNanoseconds)
{
    public (uint Rtp, ulong Nanoseconds) Position(long packets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(packets);
        ulong samples = checked((ulong)packets * 352UL);
        ulong elapsed = checked(samples / 44100 * 1_000_000_000UL
            + samples % 44100 * 1_000_000_000UL / 44100);
        return (unchecked(StartRtp + (uint)samples), checked(StartNanoseconds + elapsed));
    }
}
