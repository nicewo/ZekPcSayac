using ZekPcSayac.Core;

namespace ZekPcSayac.Core.Tests;

public class DateKeysTests
{
    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utc) => UtcNow = utc;
        public DateTime UtcNow { get; }
        public DateTime LocalNow => UtcNow;
    }

    [Fact]
    public void UTC_2130_Turkiye_saatiyle_ertesi_gundur()
    {
        var clock = new FixedClock(new DateTime(2026, 11, 16, 21, 30, 0, DateTimeKind.Utc));

        Assert.Equal("2026-11-17", DateKeys.For(clock));
    }

    [Fact]
    public void UTC_2059_Turkiye_saatiyle_ayni_gundur()
    {
        var clock = new FixedClock(new DateTime(2026, 11, 16, 20, 59, 0, DateTimeKind.Utc));

        Assert.Equal("2026-11-16", DateKeys.For(clock));
    }

    [Fact]
    public void Tarih_bicimi_yyyy_MM_dd_olur()
    {
        Assert.Equal("2026-01-05", DateKeys.For(new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc)));
    }
}
