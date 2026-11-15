namespace ZekPcSayac.Core;

/// <summary>
/// Sunucu zamanına göre düzeltilmiş saat. Firestore yanıtındaki Date başlığından hesaplanan
/// ofset uygulanır. Sistem saati son güvenilen andan 5 dakikadan fazla geri alınırsa
/// saat son güvenilen zamanda donar; böylece saat geri alınarak gün veya süre kazanılamaz.
/// </summary>
public sealed class TrustedClock : IClock
{
    /// <summary>Bu süreden küçük geri kaymalar (ağ gecikmesi, küçük düzeltmeler) tolere edilir.</summary>
    public static readonly TimeSpan MaxBackwardDrift = TimeSpan.FromMinutes(5);

    private readonly Func<DateTime> _systemUtc;
    private readonly object _gate = new();
    private TimeSpan _serverOffset;
    private DateTime _lastTrustedUtc;

    public TrustedClock() : this(() => DateTime.UtcNow)
    {
    }

    /// <summary>Testlerde sahte sistem saati enjekte etmek için.</summary>
    public TrustedClock(Func<DateTime> systemUtc)
    {
        _systemUtc = systemUtc ?? throw new ArgumentNullException(nameof(systemUtc));
    }

    /// <summary>Sunucu zamanı ile sistem UTC saati arasındaki fark (sunucu - sistem).</summary>
    public TimeSpan ServerOffset
    {
        get { lock (_gate) return _serverOffset; }
    }

    /// <summary>Bilinen en son güvenilir UTC zamanı. Hiç yoksa <see cref="DateTime.MinValue"/>.</summary>
    public DateTime LastTrustedUtc
    {
        get { lock (_gate) return _lastTrustedUtc; }
    }

    public DateTime UtcNow
    {
        get
        {
            lock (_gate)
            {
                DateTime now = _systemUtc() + _serverOffset;
                bool hasTrust = _lastTrustedUtc != default;

                if (hasTrust && now < _lastTrustedUtc - MaxBackwardDrift)
                    return _lastTrustedUtc;

                if (!hasTrust || now > _lastTrustedUtc)
                    _lastTrustedUtc = now;

                return now;
            }
        }
    }

    public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, TimeZoneInfo.Local);

    /// <summary>Sunucudan doğrulanmış yeni ofseti uygular ve son güvenilir zamanı sunucu zamanına çeker.</summary>
    public void SetServerOffset(TimeSpan offset)
    {
        lock (_gate)
        {
            _serverOffset = offset;
            _lastTrustedUtc = _systemUtc() + offset;
        }
    }

    /// <summary>Kayıtlı durumdan (ofset ve son güvenilir zaman) geri yükler.</summary>
    public void Restore(TimeSpan serverOffset, DateTime lastTrustedUtc)
    {
        lock (_gate)
        {
            _serverOffset = serverOffset;
            _lastTrustedUtc = lastTrustedUtc == default
                ? default
                : DateTime.SpecifyKind(lastTrustedUtc, DateTimeKind.Utc);
        }
    }
}
