using ZekPcSayac.Core;

namespace ZekPcSayac.Core.Tests;

public class LockDeciderTests
{
    // 2026-11-16 Pazartesi.
    private static readonly AllowedWindow[] Weekdays =
        [new AllowedWindow([1, 2, 3, 4, 5], new TimeOnly(15, 0), new TimeOnly(20, 0))];

    private static DateTime At(int day, int h, int m = 0, int s = 0) => new(2026, 11, day, h, m, s);

    [Fact]
    public void Disabled_QuotaOk_IsNone()
    {
        var d = LockDecider.Decide(At(16, 3), false, Weekdays, false);
        Assert.Equal(LockReason.None, d.Reason);
        Assert.Null(d.TimeUntilWindowEnd);
    }

    [Fact]
    public void Disabled_QuotaExhausted_IsQuota()
    {
        Assert.Equal(LockReason.Quota, LockDecider.Decide(At(16, 3), false, Weekdays, true).Reason);
    }

    [Fact]
    public void Enabled_Inside_QuotaOk_IsNone_WithTimeLeft()
    {
        var d = LockDecider.Decide(At(16, 19, 30), true, Weekdays, false);
        Assert.Equal(LockReason.None, d.Reason);
        Assert.Equal(TimeSpan.FromMinutes(30), d.TimeUntilWindowEnd);
    }

    [Fact]
    public void Enabled_Inside_QuotaExhausted_IsQuota()
    {
        Assert.Equal(LockReason.Quota, LockDecider.Decide(At(16, 16), true, Weekdays, true).Reason);
    }

    [Fact]
    public void Enabled_Outside_IsOutsideWindow_WithNextStart()
    {
        var d = LockDecider.Decide(At(16, 21), true, Weekdays, false);
        Assert.Equal(LockReason.OutsideWindow, d.Reason);
        Assert.Equal(At(17, 15), d.NextWindowStart);
    }

    [Fact]
    public void Enabled_Outside_WinsOverQuota()
    {
        Assert.Equal(LockReason.OutsideWindow, LockDecider.Decide(At(16, 21), true, Weekdays, true).Reason);
    }

    [Fact]
    public void Enabled_EmptyList_IsOutsideWindow()
    {
        var d = LockDecider.Decide(At(16, 16), true, [], false);
        Assert.Equal(LockReason.OutsideWindow, d.Reason);
        Assert.Null(d.NextWindowStart);
    }

    [Theory]
    [InlineData(600, 0)]
    [InlineData(301, 0)]
    [InlineData(300, 1)]
    [InlineData(61, 1)]
    [InlineData(60, 2)]
    [InlineData(1, 2)]
    public void WarningLevel_Thresholds(int seconds, int expected)
    {
        Assert.Equal(expected, LockDecider.WarningLevel(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void WarningLevel_Null_IsZero() => Assert.Equal(0, LockDecider.WarningLevel(null));

    [Fact]
    public void FormatNextStart_UsesTurkishDayName()
    {
        Assert.Equal("Pazartesi 15:00", LockDecider.FormatNextStart(At(16, 15)));
        Assert.Equal("Pazar 09:30", LockDecider.FormatNextStart(At(22, 9, 30)));
    }
}
