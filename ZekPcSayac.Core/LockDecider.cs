namespace ZekPcSayac.Core;

/// <summary>Ekranın neden kilitli olması gerektiği.</summary>
public enum LockReason
{
    None,
    /// <summary>Süre bitti veya kullanım durduruldu.</summary>
    Quota,
    /// <summary>İzinli saat aralığının dışında.</summary>
    OutsideWindow
}

/// <param name="Reason">Kilit sebebi; None ise ekran açık kalabilir.</param>
/// <param name="TimeUntilWindowEnd">Saat kısıtı açıkken ve aralık içindeyken kapanışa kalan süre; aksi halde null.</param>
/// <param name="NextWindowStart">SaatDışı iken bir sonraki açılış (yerel Türkiye saati); bilinmiyorsa null.</param>
public sealed record LockDecision(LockReason Reason, TimeSpan? TimeUntilWindowEnd, DateTime? NextWindowStart);

/// <summary>Kota ve saat aralığı kurallarından kilit kararını üreten saf fonksiyon.</summary>
public static class LockDecider
{
    /// <summary>Bu süre ve altında kapanışa kalan süre uyarı (turuncu) sayılır.</summary>
    public static readonly TimeSpan WarnThreshold = TimeSpan.FromMinutes(5);

    /// <summary>Bu süre ve altında kapanışa kalan süre kritik (kırmızı) sayılır.</summary>
    public static readonly TimeSpan CriticalThreshold = TimeSpan.FromSeconds(60);

    /// <param name="localNow">Türkiye yerel saati.</param>
    /// <param name="windowsEnabled">false ise saat kısıtı yoktur (eski davranış).</param>
    /// <param name="quotaExhausted">Süre bitti veya kullanım durduruldu.</param>
    public static LockDecision Decide(
        DateTime localNow,
        bool windowsEnabled,
        IReadOnlyList<AllowedWindow> windows,
        bool quotaExhausted)
    {
        ArgumentNullException.ThrowIfNull(windows);

        if (!windowsEnabled)
        {
            return new LockDecision(quotaExhausted ? LockReason.Quota : LockReason.None, null, null);
        }

        // Açık ama boş liste: hiçbir aralık izinli değil; en güvenli (kilitli) davranış.
        if (windows.Count == 0)
        {
            return new LockDecision(LockReason.OutsideWindow, null, null);
        }

        if (!WindowEvaluator.IsAllowed(localNow, windows))
        {
            return new LockDecision(LockReason.OutsideWindow, null, WindowEvaluator.NextWindowStart(localNow, windows));
        }

        TimeSpan? untilEnd = WindowEvaluator.TimeUntilWindowEnd(localNow, windows);
        return new LockDecision(quotaExhausted ? LockReason.Quota : LockReason.None, untilEnd, null);
    }

    /// <summary>Kapanışa kalan süreye göre uyarı seviyesi: 0 normal, 1 turuncu, 2 kırmızı.</summary>
    public static int WarningLevel(TimeSpan? timeUntilWindowEnd)
    {
        if (timeUntilWindowEnd is not TimeSpan left) return 0;
        if (left <= CriticalThreshold) return 2;
        if (left <= WarnThreshold) return 1;
        return 0;
    }

    private static readonly string[] DayNames =
        ["Pazar", "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi"];

    /// <summary>"Pazartesi 18:00" biçiminde Türkçe gün adı ve saat.</summary>
    public static string FormatNextStart(DateTime localStart) =>
        $"{DayNames[(int)localStart.DayOfWeek]} {localStart:HH:mm}";
}
