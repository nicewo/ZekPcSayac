using ZekPcSayac.Core;

namespace ZekPcSayac.Core.Tests;

public class TrustedClockTests
{
    private sealed class FakeSystemClock
    {
        public DateTime Utc { get; set; } = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
    }

    [Fact]
    public void Sunucu_ofseti_uygulanir()
    {
        var system = new FakeSystemClock();
        var clock = new TrustedClock(() => system.Utc);

        clock.SetServerOffset(TimeSpan.FromHours(2));

        Assert.Equal(new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc), clock.UtcNow);
        Assert.Equal(TimeSpan.FromHours(2), clock.ServerOffset);
    }

    [Fact]
    public void Saat_geri_alininca_son_guvenilir_zaman_dondurulur()
    {
        var system = new FakeSystemClock();
        var clock = new TrustedClock(() => system.Utc);
        clock.SetServerOffset(TimeSpan.Zero);

        // Yarın gününe ileri al, sonra bir gün geri al.
        system.Utc = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        DateTime forward = clock.UtcNow;
        system.Utc = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal(forward, clock.UtcNow);
        Assert.Equal("2026-11-17", DateKeys.For(clock));
    }

    [Fact]
    public void Kucuk_geri_kayma_tolere_edilir()
    {
        var system = new FakeSystemClock();
        var clock = new TrustedClock(() => system.Utc);
        clock.SetServerOffset(TimeSpan.Zero);
        _ = clock.UtcNow;

        system.Utc = system.Utc.AddMinutes(-3);

        Assert.Equal(system.Utc, clock.UtcNow);
    }

    [Fact]
    public void Kayitli_durumdan_geri_yuklenince_geri_alma_yine_engellenir()
    {
        var system = new FakeSystemClock();
        var lastTrusted = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        var clock = new TrustedClock(() => system.Utc);
        clock.Restore(TimeSpan.Zero, lastTrusted);

        system.Utc = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal(lastTrusted, clock.UtcNow);
        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);
    }

    [Fact]
    public void Sunucu_ofseti_sistem_geri_alindiktan_sonra_dogru_zamani_verir()
    {
        var system = new FakeSystemClock();
        var clock = new TrustedClock(() => system.Utc);
        clock.SetServerOffset(TimeSpan.Zero);

        // Kullanıcı saati bir gün geri aldı; sunucu yanıtı gerçek zamanı verir.
        system.Utc = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
        clock.SetServerOffset(new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc) - system.Utc);

        Assert.Equal(new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc), clock.UtcNow);
    }
}
