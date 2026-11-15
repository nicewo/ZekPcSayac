namespace ZekPcSayac.Core;

/// <summary>Firestore <c>deviceConfig/{deviceId}</c> belgesinin uygulamanın kullandığı kısmı.</summary>
/// <param name="ExitPassword">Null ise çıkış şifresi tanımlı değildir; uygulama kapatılamaz.</param>
/// <param name="AllowedWindowsEnabled">false ise saat kısıtı yoktur.</param>
/// <param name="AllowedWindows">İzinli saat aralıkları (yalnızca AllowedWindowsEnabled iken anlamlı).</param>
public sealed record DeviceConfig(
    int DailySeconds,
    int BonusSeconds,
    ExitPasswordHash? ExitPassword,
    bool AllowedWindowsEnabled = false,
    IReadOnlyList<AllowedWindow>? AllowedWindows = null)
{
    public IReadOnlyList<AllowedWindow> AllowedWindows { get; init; } = AllowedWindows ?? [];
}
