using ZekPcSayac.Core;

namespace ZekPcSayac.Core.Tests;

public class ExitPasswordAttemptLimiterTests
{
    private sealed class FakeClock : IClock
    {
        public DateTime Now { get; set; } = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Now;
        public DateTime LocalNow => Now.ToLocalTime();
    }

    [Fact]
    public void Dort_yanlis_denemede_kilit_yoktur()
    {
        var clock = new FakeClock();
        var limiter = new ExitPasswordAttemptLimiter(clock);

        for (int i = 0; i < ExitPasswordAttemptLimiter.MaxFailures - 1; i++)
            limiter.RecordFailure();

        Assert.Null(limiter.RemainingLock());
    }

    [Fact]
    public void Bes_yanlis_denemede_60_saniye_kilitlenir()
    {
        var clock = new FakeClock();
        var limiter = new ExitPasswordAttemptLimiter(clock);

        for (int i = 0; i < 5; i++)
            limiter.RecordFailure();

        Assert.Equal(TimeSpan.FromSeconds(60), limiter.RemainingLock());

        clock.Now = clock.Now.AddSeconds(30);
        Assert.Equal(TimeSpan.FromSeconds(30), limiter.RemainingLock());
    }

    [Fact]
    public void Kilit_suresi_dolunca_bes_yeni_deneme_hakki_verilir()
    {
        var clock = new FakeClock();
        var limiter = new ExitPasswordAttemptLimiter(clock);
        for (int i = 0; i < 5; i++)
            limiter.RecordFailure();

        clock.Now = clock.Now.AddSeconds(60);
        Assert.Null(limiter.RemainingLock());

        for (int i = 0; i < 4; i++)
            limiter.RecordFailure();
        Assert.Null(limiter.RemainingLock());

        limiter.RecordFailure();
        Assert.NotNull(limiter.RemainingLock());
    }

    [Fact]
    public void Basarili_giris_sayaci_sifirlar()
    {
        var clock = new FakeClock();
        var limiter = new ExitPasswordAttemptLimiter(clock);
        for (int i = 0; i < 4; i++)
            limiter.RecordFailure();

        limiter.RecordSuccess();
        limiter.RecordFailure();

        Assert.Null(limiter.RemainingLock());
    }

    [Fact]
    public void Kilitliyken_yanlis_deneme_kilit_suresini_uzatmaz()
    {
        var clock = new FakeClock();
        var limiter = new ExitPasswordAttemptLimiter(clock);
        for (int i = 0; i < 5; i++)
            limiter.RecordFailure();

        clock.Now = clock.Now.AddSeconds(10);
        limiter.RecordFailure();

        Assert.Equal(TimeSpan.FromSeconds(50), limiter.RemainingLock());
    }
}
