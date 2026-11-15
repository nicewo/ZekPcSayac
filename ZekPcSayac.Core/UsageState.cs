namespace ZekPcSayac.Core;

/// <summary>Yerel kullanım durumu. Diske <see cref="SignedStateStore"/> ile imzalı yazılır.</summary>
public sealed class UsageState
{
    public int BaseSeconds { get; set; }
    public int BonusSeconds { get; set; }
    public int RemainingSeconds { get; set; }
    public bool IsPaused { get; set; }
    public DateTime LastStartedUtc { get; set; }
    public string DateKey { get; set; } = "";
    public string LastAppliedRemainingCommandId { get; set; } = "";
    public bool AudioWasMutedBeforePause { get; set; }
    public bool AudioWasMutedByApp { get; set; }
    /// <summary>Sunucu zamanı ofseti (<see cref="TrustedClock.ServerOffset"/>). Eski dosyalarda 0.</summary>
    public long ServerOffsetTicks { get; set; }
    /// <summary>Son güvenilir UTC zamanı. Eski dosyalarda <see cref="DateTime.MinValue"/>.</summary>
    public DateTime LastTrustedUtc { get; set; }
    /// <summary>Son başarılı okunan saat kısıtı ayarı (offline kullanım için). Eski dosyalarda false.</summary>
    public bool AllowedWindowsEnabled { get; set; }
    public List<AllowedWindow> AllowedWindows { get; set; } = [];
}
