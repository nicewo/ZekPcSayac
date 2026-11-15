namespace ZekPcSayac.Core;

/// <summary>
/// Saat aralığı kurallarını yerel saate göre değerlendirir.
/// Boş pencere listesi her zaman izinli kabul edilir.
/// Bitişik/çakışan aralıklar birleştirilerek tek bir izin dönemi gibi değerlendirilir.
/// localNow yerel (Türkiye) saat olmalıdır.
/// </summary>
public static class WindowEvaluator
{
    private static readonly TimeSpan NextStartHorizon = TimeSpan.FromDays(7);

    /// <summary>Verilen yerel an izinli bir aralığın içindeyse true.</summary>
    public static bool IsAllowed(DateTime localNow, IReadOnlyList<AllowedWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (windows.Count == 0)
        {
            return true;
        }

        return FindRunContaining(localNow, BuildRuns(localNow, windows)) is not null;
    }

    /// <summary>Şu an bir aralığın içindeyse, izin döneminin bitmesine kalan süre; değilse null.</summary>
    public static TimeSpan? TimeUntilWindowEnd(DateTime localNow, IReadOnlyList<AllowedWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (windows.Count == 0)
        {
            return null;
        }

        Run? run = FindRunContaining(localNow, BuildRuns(localNow, windows));
        return run is null ? null : run.End - localNow;
    }

    /// <summary>
    /// localNow'dan sonraki ilk izin başlangıcını (7 gün içinde) döndürür.
    /// Şu an izinliyse, mevcut dönemden sonraki ilk başlangıç döner. Bulunamazsa null.
    /// </summary>
    public static DateTime? NextWindowStart(DateTime localNow, IReadOnlyList<AllowedWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (windows.Count == 0)
        {
            return null;
        }

        DateTime limit = localNow + NextStartHorizon;
        foreach (Run run in BuildRuns(localNow, windows))
        {
            if (run.Start > localNow && run.Start <= limit)
            {
                return run.Start;
            }
        }

        return null;
    }

    private sealed record Run(DateTime Start, DateTime End);

    private static Run? FindRunContaining(DateTime localNow, List<Run> runs)
    {
        foreach (Run run in runs)
        {
            if (run.Start <= localNow && localNow < run.End)
            {
                return run;
            }
        }

        return null;
    }

    /// <summary>
    /// localNow etrafında (bir gün öncesi ile yedi gün sonrası arası) pencere örneklerini üretir,
    /// sıralar ve bitişik/çakışanları birleştirir.
    /// Bir örnek en fazla 24 saat sürer; bu yüzden içeriden başlayan bir aralığın başlangıcı
    /// en az bir gün önceki örnekte yer alır.
    /// </summary>
    private static List<Run> BuildRuns(DateTime localNow, IReadOnlyList<AllowedWindow> windows)
    {
        var intervals = new List<Run>();
        DateTime firstDay = localNow.Date.AddDays(-2);

        for (int i = 0; i <= 10; i++)
        {
            DateTime day = firstDay.AddDays(i);
            int isoDay = IsoDayOfWeek(day);

            foreach (AllowedWindow window in windows)
            {
                if (Array.IndexOf(window.Days, isoDay) < 0)
                {
                    continue;
                }

                DateTime start = day + window.Start.ToTimeSpan();
                DateTime end = day + window.End.ToTimeSpan();
                if (window.End <= window.Start)
                {
                    end = end.AddDays(1);
                }

                intervals.Add(new Run(start, end));
            }
        }

        intervals.Sort((a, b) => a.Start.CompareTo(b.Start));

        var runs = new List<Run>();
        foreach (Run interval in intervals)
        {
            if (runs.Count > 0 && interval.Start <= runs[^1].End)
            {
                if (interval.End > runs[^1].End)
                {
                    runs[^1] = runs[^1] with { End = interval.End };
                }
            }
            else
            {
                runs.Add(interval);
            }
        }

        return runs;
    }

    private static int IsoDayOfWeek(DateTime day) =>
        day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
}
