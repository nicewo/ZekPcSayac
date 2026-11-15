namespace ZekPcSayac.Core;

/// <summary>Zaman kaynağı. Testlerde sahte saat enjekte edilebilmesi için soyutlanmıştır.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
    DateTime LocalNow { get; }
}

/// <summary>Sistem saatini kullanan <see cref="IClock"/> uygulaması.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime LocalNow => DateTime.Now;
}
