using ZekPcSayac.Core;

namespace ZekPcSayac.Core.Tests;

public class WindowEvaluatorTests
{
    // 2026-11-16 bir Pazartesi (ISO 1).
    private static readonly DateTime Monday = new(2026, 11, 16);

    private static readonly AllowedWindow Weekdays9To17 =
        new([1, 2, 3, 4, 5], new TimeOnly(9, 0), new TimeOnly(17, 0));

    private static DateTime At(DateTime day, int hour, int minute, int second = 0) =>
        day.Date.AddHours(hour).AddMinutes(minute).AddSeconds(second);

    [Fact]
    public void Bos_liste_her_zaman_izinlidir()
    {
        Assert.True(WindowEvaluator.IsAllowed(At(Monday, 3, 0), []));
        Assert.Null(WindowEvaluator.TimeUntilWindowEnd(At(Monday, 3, 0), []));
        Assert.Null(WindowEvaluator.NextWindowStart(At(Monday, 3, 0), []));
    }

    [Fact]
    public void Hafta_ici_pencere_icinde_izinlidir()
    {
        Assert.True(WindowEvaluator.IsAllowed(At(Monday, 10, 0), [Weekdays9To17]));
    }

    [Fact]
    public void Hafta_sonu_hafta_ici_penceresinde_izinsizdir()
    {
        DateTime saturday = Monday.AddDays(5);
        Assert.False(WindowEvaluator.IsAllowed(At(saturday, 10, 0), [Weekdays9To17]));
    }

    [Fact]
    public void Baslangic_dahildir()
    {
        Assert.True(WindowEvaluator.IsAllowed(At(Monday, 9, 0, 0), [Weekdays9To17]));
        Assert.False(WindowEvaluator.IsAllowed(At(Monday, 8, 59, 59), [Weekdays9To17]));
    }

    [Fact]
    public void Bitis_haric_tutulur()
    {
        Assert.True(WindowEvaluator.IsAllowed(At(Monday, 16, 59, 59), [Weekdays9To17]));
        Assert.False(WindowEvaluator.IsAllowed(At(Monday, 17, 0, 0), [Weekdays9To17]));
    }

    [Fact]
    public void Gece_yarisini_gecen_aralik_ertesi_gun_sabahini_kapsar()
    {
        var window = new AllowedWindow([1], new TimeOnly(21, 0), new TimeOnly(0, 30));

        Assert.True(WindowEvaluator.IsAllowed(At(Monday.AddDays(1), 0, 15), [window]));
    }

    [Fact]
    public void Gece_yarisini_gecen_aralikta_ikinci_gun_baslangici_izinsizdir()
    {
        var window = new AllowedWindow([1], new TimeOnly(21, 0), new TimeOnly(0, 30));

        Assert.False(WindowEvaluator.IsAllowed(At(Monday.AddDays(1), 21, 30), [window]));
    }

    [Fact]
    public void Gece_yarisini_gecen_aralik_baslangic_gununde_aksam_izinli_sabah_izinsiz()
    {
        var window = new AllowedWindow([1], new TimeOnly(21, 0), new TimeOnly(0, 30));

        Assert.True(WindowEvaluator.IsAllowed(At(Monday, 23, 59), [window]));
        Assert.False(WindowEvaluator.IsAllowed(At(Monday, 0, 10), [window]));
    }

    [Fact]
    public void Pazar_den_pazartesiye_gecis_aralik_pazartesi_sabahini_kapsar()
    {
        var window = new AllowedWindow([7], new TimeOnly(22, 0), new TimeOnly(1, 0));
        DateTime sunday = Monday.AddDays(-1);

        Assert.True(WindowEvaluator.IsAllowed(At(sunday, 23, 0), [window]));
        Assert.True(WindowEvaluator.IsAllowed(At(Monday, 0, 30), [window]));
        Assert.False(WindowEvaluator.IsAllowed(At(Monday, 1, 0), [window]));
    }

    [Fact]
    public void Kalan_sure_pencere_icinde_bitisie_kadar_hesaplanir()
    {
        TimeSpan? remaining = WindowEvaluator.TimeUntilWindowEnd(At(Monday, 10, 0), [Weekdays9To17]);

        Assert.Equal(TimeSpan.FromHours(7), remaining);
    }

    [Fact]
    public void Kalan_sure_gece_yarisini_gecen_aralikta_dogru_hesaplanir()
    {
        var window = new AllowedWindow([1], new TimeOnly(21, 0), new TimeOnly(0, 30));

        TimeSpan? remaining = WindowEvaluator.TimeUntilWindowEnd(At(Monday, 23, 0), [window]);

        Assert.Equal(TimeSpan.FromMinutes(90), remaining);
    }

    [Fact]
    public void Pencere_disindayken_kalan_sure_null_doner()
    {
        Assert.Null(WindowEvaluator.TimeUntilWindowEnd(At(Monday, 20, 0), [Weekdays9To17]));
    }

    [Fact]
    public void Bitisik_pencereler_tek_donem_olarak_degerlendirilir()
    {
        var morning = new AllowedWindow([1], new TimeOnly(9, 0), new TimeOnly(12, 0));
        var afternoon = new AllowedWindow([1], new TimeOnly(12, 0), new TimeOnly(17, 0));

        TimeSpan? remaining = WindowEvaluator.TimeUntilWindowEnd(At(Monday, 10, 0), [morning, afternoon]);

        Assert.Equal(TimeSpan.FromHours(7), remaining);
    }

    [Fact]
    public void Sonraki_baslangic_hafta_sonundan_sonra_pazartesi_dokuzdur()
    {
        DateTime saturday = Monday.AddDays(5);

        DateTime? next = WindowEvaluator.NextWindowStart(At(saturday, 10, 0), [Weekdays9To17]);

        Assert.Equal(At(Monday.AddDays(7), 9, 0), next);
    }

    [Fact]
    public void Izinliyken_sonraki_baslangic_mevcut_donemden_sonradir()
    {
        var mondayTuesday = new AllowedWindow([1, 2], new TimeOnly(9, 0), new TimeOnly(17, 0));

        DateTime? next = WindowEvaluator.NextWindowStart(At(Monday, 10, 0), [mondayTuesday]);

        Assert.Equal(At(Monday.AddDays(1), 9, 0), next);
    }

    [Fact]
    public void Gece_yarisini_gecen_aralikta_sonraki_baslangic_dogrudur()
    {
        var window = new AllowedWindow([1], new TimeOnly(21, 0), new TimeOnly(0, 30));

        DateTime? next = WindowEvaluator.NextWindowStart(At(Monday, 0, 45), [window]);

        Assert.Equal(At(Monday, 21, 0), next);
    }
}
