using ZekPcSayac.Core;

namespace ZekPcSayac.Core.Tests;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_gercek_saate_5_saniye_yakin()
    {
        var clock = new SystemClock();

        var before = DateTime.UtcNow;
        var actual = clock.UtcNow;
        var after = DateTime.UtcNow;

        Assert.Equal(DateTimeKind.Utc, actual.Kind);
        Assert.InRange(actual, before.AddSeconds(-5), after.AddSeconds(5));
    }
}
