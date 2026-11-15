namespace ZekPcSayac.Core;

/// <summary>Firestore <c>deviceConfig/{deviceId}</c> belgesinin uygulamanın kullandığı kısmı.</summary>
/// <param name="ExitPassword">Null ise çıkış şifresi tanımlı değildir; uygulama kapatılamaz.</param>
public sealed record DeviceConfig(int DailySeconds, int BonusSeconds, ExitPasswordHash? ExitPassword);
