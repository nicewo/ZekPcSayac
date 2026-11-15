namespace ZekPcSayac.Core;

/// <summary>
/// Çıkış şifresi denemelerini sınırlar: <see cref="MaxFailures"/> yanlış denemeden sonra
/// <see cref="LockDuration"/> süresince kilitler. Durum bellektedir (uygulama yeniden başlayınca sıfırlanır).
/// </summary>
public sealed class ExitPasswordAttemptLimiter
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromSeconds(60);

    private readonly IClock _clock;
    private int _failures;
    private DateTime? _lockedUntilUtc;

    public ExitPasswordAttemptLimiter(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Kilit aktifse kalan süreyi döner; değilse null.</summary>
    public TimeSpan? RemainingLock()
    {
        if (_lockedUntilUtc is null) return null;

        TimeSpan remaining = _lockedUntilUtc.Value - _clock.UtcNow;
        if (remaining > TimeSpan.Zero) return remaining;

        // Kilit süresi doldu: yeni 5 deneme hakkı verilir.
        _lockedUntilUtc = null;
        _failures = 0;
        return null;
    }

    public void RecordFailure()
    {
        if (RemainingLock() is not null) return;

        _failures++;
        if (_failures >= MaxFailures)
        {
            _lockedUntilUtc = _clock.UtcNow + LockDuration;
            _failures = 0;
        }
    }

    public void RecordSuccess()
    {
        _failures = 0;
        _lockedUntilUtc = null;
    }
}
