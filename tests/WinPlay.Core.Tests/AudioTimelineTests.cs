using WinPlay.Core.Raop;
using Xunit;

namespace WinPlay.Core.Tests;

public class AudioTimelineTests
{
    [Fact]
    public void WholeSecondsOfSamplesAdvanceClockExactly()
    {
        var timeline = new AudioTimeline(1234, 5_000_000_000);
        // 11025 packets = 88 seconds at 352 samples per packet.
        var point = timeline.Position(11025);
        Assert.Equal(1234U + 11025U * 352U, point.Rtp);
        Assert.Equal(93_000_000_000UL, point.Nanoseconds);
    }

    [Fact]
    public void RtpWrapDoesNotResetMonotonicTime()
    {
        var timeline = new AudioTimeline(uint.MaxValue - 100, 10_000_000_000);
        var point = timeline.Position(1);
        Assert.Equal(251U, point.Rtp);
        Assert.Equal(10_000_000_000UL + 352UL * 1_000_000_000 / 44100, point.Nanoseconds);
    }

    [Fact]
    public void LongRunningStreamDoesNotOverflowIntermediateMultiplication()
    {
        var timeline = new AudioTimeline(0, 1);
        long packets = 44100L * 86400 * 365 / 352;
        var point = timeline.Position(packets);
        ulong expected = 1 + (ulong)((decimal)packets * 352 * 1_000_000_000 / 44100);
        Assert.Equal(expected, point.Nanoseconds);
    }
}
