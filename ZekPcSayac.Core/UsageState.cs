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
}
