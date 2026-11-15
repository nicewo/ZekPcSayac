using System.Globalization;

namespace ZekPcSayac.Core;

/// <summary>Kullanım günü anahtarı (yyyy-MM-dd) üretir. Gün sınırı Türkiye saatine göre belirlenir.</summary>
public static class DateKeys
{
    private const string WindowsTurkeyZoneId = "Turkey Standard Time";
    private const string IanaTurkeyZoneId = "Europe/Istanbul";

    private static readonly Lazy<TimeZoneInfo> TurkeyZone = new(ResolveTurkeyZone);

    public static string For(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return For(clock.UtcNow);
    }

    /// <summary>Verilen UTC anı için Türkiye saatine göre gün anahtarını döndürür.</summary>
    public static string For(DateTime utc)
    {
        DateTime utcKind = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        DateTime turkeyTime = TimeZoneInfo.ConvertTimeFromUtc(utcKind, TurkeyZone.Value);
        return turkeyTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Verilen UTC anının Türkiye yerel saatini döndürür.</summary>
    public static DateTime TurkeyLocal(DateTime utc)
    {
        DateTime utcKind = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utcKind, TurkeyZone.Value);
    }

    private static TimeZoneInfo ResolveTurkeyZone()
    {
        foreach (string id in new[] { WindowsTurkeyZoneId, IanaTurkeyZoneId })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        // Türkiye 2016'dan beri sabit UTC+3 kullanır (yaz saati uygulaması yok).
        return TimeZoneInfo.CreateCustomTimeZone("UTC+03", TimeSpan.FromHours(3), "UTC+03", "UTC+03");
    }
}
