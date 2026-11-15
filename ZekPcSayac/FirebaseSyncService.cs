using System;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ZekPcSayac.Core;

namespace ZekPcSayac;

internal sealed class FirebaseSyncService
{
    private const string ProjectId = "zekpcsayac";
    private const string ApiKey = "AIzaSyDyfpxmS8-nl2tQfuzHV8or8g-e7e7lx5M";
    private const string DeviceId = "zekeriyas-pc";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly string _credentialPath;
    private readonly string _exitHashPath;
    private readonly TrustedClock _clock;
    private string? _email;
    private string? _password;
    private string? _uid;
    private string? _idToken;
    private DateTime _tokenExpiresAt = DateTime.MinValue;

    public FirebaseSyncService(string appDirectory, TrustedClock clock)
    {
        _credentialPath = System.IO.Path.Combine(appDirectory, "firebase-login.bin");
        _exitHashPath = System.IO.Path.Combine(appDirectory, "exit-hash.bin");
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        LoadCredentials();
    }

    /// <summary>Son başarılı Firestore yanıtındaki Date başlığından hesaplanan sunucu ofseti (sunucu - sistem UTC). Yoksa null.</summary>
    public TimeSpan? ServerUtcOffset { get; private set; }

    /// <summary>Son başarılı senkronizasyonda saklanan çıkış şifresi hash'i (offline çıkış için). Yoksa null.</summary>
    public ExitPasswordHash? LoadCachedExitPassword()
    {
        try
        {
            if (!System.IO.File.Exists(_exitHashPath)) return null;
            byte[] encrypted = System.IO.File.ReadAllBytes(_exitHashPath);
            byte[] clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var saved = JsonSerializer.Deserialize<ExitPasswordHash>(clear);
            CryptographicOperations.ZeroMemory(clear);
            return saved;
        }
        catch
        {
            return null;
        }
    }

    private void SaveCachedExitPassword(ExitPasswordHash? hash)
    {
        try
        {
            if (hash is null)
            {
                if (System.IO.File.Exists(_exitHashPath)) System.IO.File.Delete(_exitHashPath);
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(hash));
            byte[] protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            System.IO.File.WriteAllBytes(_exitHashPath, protectedBytes);
        }
        catch
        {
            // Önbellek yazılamazsa bellekteki hash ile çalışılır; yalnızca offline çıkış etkilenir.
        }
    }

    public bool HasCredentials => !string.IsNullOrWhiteSpace(_email) && !string.IsNullOrWhiteSpace(_password);
    public string? SavedEmail => _email;

    public void SaveCredentials(string email, string password)
    {
        _email = email.Trim();
        _password = password;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Credentials { Email = _email, Password = _password }));
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        System.IO.File.WriteAllBytes(_credentialPath, protectedBytes);
        CryptographicOperations.ZeroMemory(bytes);
        _idToken = null;
        _tokenExpiresAt = DateTime.MinValue;
    }

    public void ClearCredentials()
    {
        _email = null;
        _password = null;
        _idToken = null;
        _tokenExpiresAt = DateTime.MinValue;
        try { if (System.IO.File.Exists(_credentialPath)) System.IO.File.Delete(_credentialPath); } catch { }
    }

    public async Task<FirebaseSnapshot> ConnectAndReadAsync()
    {
        await SignInAsync();
        DeviceConfig config = await ReadConfigAsync();
        string today = DateKeys.For(_clock);

        JsonDocument? state = null;
        try { state = await GetDocumentAsync($"deviceState/{DeviceId}"); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }

        int? remaining = null;
        bool? paused = null;
        string dateKey = today;
        if (state is not null)
        {
            using (state)
            {
                var sf = state.RootElement.GetProperty("fields");
                remaining = GetNullableInt(sf, "remainingSeconds");
                paused = GetNullableBool(sf, "isPaused");
                var cloudDate = GetString(sf, "dateKey");
                if (!string.IsNullOrWhiteSpace(cloudDate)) dateKey = cloudDate;
            }
        }

        return new FirebaseSnapshot(config.DailySeconds, config.BonusSeconds, remaining, paused, dateKey, state is null, config.ExitPassword,
            config.AllowedWindowsEnabled, config.AllowedWindows);
    }

    public async Task<DeviceConfig> ReadConfigAsync()
    {
        await SignInAsync();
        using var config = await GetDocumentAsync($"deviceConfig/{DeviceId}");
        var fields = config.RootElement.GetProperty("fields");
        string deviceUid = GetString(fields, "deviceAuthUid");
        if (!string.Equals(deviceUid, _uid, StringComparison.Ordinal))
            throw new InvalidOperationException("Bu Firebase hesabı cihaz için tanımlı değil. Web panelindeki PC UID ayarını kontrol et.");

        int dailyMinutes = Math.Clamp(GetInt(fields, "dailyLimitMinutes", 120), 0, 1440);
        int bonusMinutes = Math.Clamp(GetInt(fields, "bonusMinutes", 0), 0, 1440);
        string bonusDateKey = GetString(fields, "bonusDateKey");
        string today = DateKeys.For(_clock);
        if (!string.Equals(bonusDateKey, today, StringComparison.Ordinal))
            bonusMinutes = 0;

        ExitPasswordHash? exitPassword = GetExitPassword(fields);
        SaveCachedExitPassword(exitPassword);

        bool windowsEnabled = GetNullableBool(fields, "allowedWindowsEnabled") ?? false;
        List<AllowedWindow> windows = [];
        if (windowsEnabled)
        {
            if (fields.TryGetProperty("allowedWindows", out var windowsField)
                && windowsField.TryGetProperty("arrayValue", out var windowsArray))
            {
                (windows, int skipped) = AllowedWindowParser.Parse(windowsArray);
                if (skipped > 0)
                    System.Diagnostics.Debug.WriteLine($"allowedWindows: {skipped} geçersiz öğe atlandı.");
            }
        }

        return new DeviceConfig(dailyMinutes * 60, bonusMinutes * 60, exitPassword, windowsEnabled, windows);
    }

    public async Task PushStateAsync(int remainingSeconds, bool isPaused, string dateKey)
    {
        await SignInAsync();
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var body = new
        {
            fields = new
            {
                remainingSeconds = new { integerValue = Math.Max(0, remainingSeconds).ToString() },
                isPaused = new { booleanValue = isPaused },
                dateKey = new { stringValue = dateKey },
                lastSeenAt = new { timestampValue = now },
                updatedAt = new { timestampValue = now },
                clientVersion = new { stringValue = "1.0.0" }
            }
        };
        string url = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/deviceState/{DeviceId}?updateMask.fieldPaths=remainingSeconds&updateMask.fieldPaths=isPaused&updateMask.fieldPaths=dateKey&updateMask.fieldPaths=lastSeenAt&updateMask.fieldPaths=updatedAt&updateMask.fieldPaths=clientVersion";
        using var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _idToken);
        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Firestore durum kaydı başarısız: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        CaptureServerTime(response);
    }

    public async Task<(string? CommandId, int? RemainingSeconds)> GetRemainingCommandAsync()
    {
        try
        {
            using var state = await GetDocumentAsync($"deviceState/{DeviceId}");
            if (!state.RootElement.TryGetProperty("fields", out var fields))
                return (null, null);
            string commandId = GetString(fields, "remainingCommandId");
            int? seconds = GetNullableInt(fields, "requestedRemainingSeconds");
            return (string.IsNullOrWhiteSpace(commandId) ? null : commandId, seconds);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, null);
        }
    }

    private async Task SignInAsync()
    {
        if (!string.IsNullOrWhiteSpace(_idToken) && DateTime.UtcNow < _tokenExpiresAt.AddMinutes(-2)) return;
        if (!HasCredentials)
            throw new InvalidOperationException("Firebase giriş bilgileri girilmemiş.");
        var response = await Http.PostAsJsonAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={ApiKey}",
            new { email = _email, password = _password, returnSecureToken = true });
        string json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Firebase girişi başarısız. E-posta/şifreyi ve Email/Password giriş yöntemini kontrol et.");
        using var doc = JsonDocument.Parse(json);
        _idToken = doc.RootElement.GetProperty("idToken").GetString();
        _uid = doc.RootElement.GetProperty("localId").GetString();
        int expiresIn = 3600;
        if (doc.RootElement.TryGetProperty("expiresIn", out var expiresElement))
            int.TryParse(expiresElement.GetString(), out expiresIn);
        _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn > 0 ? expiresIn : 3600);
    }

    private async Task<JsonDocument> GetDocumentAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/{path}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _idToken);
        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Firestore okuma başarısız: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}",
                null, response.StatusCode);
        CaptureServerTime(response);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    // Sistem saati geri alınsa bile sunucu Date başlığı gerçek zamanı verir; ofset buna göre güncellenir.
    private void CaptureServerTime(HttpResponseMessage response)
    {
        DateTimeOffset? serverDate = response.Headers.Date;
        if (serverDate is null) return;

        ServerUtcOffset = serverDate.Value.UtcDateTime - DateTime.UtcNow;
        _clock.SetServerOffset(ServerUtcOffset.Value);
    }

    private void LoadCredentials()
    {
        try
        {
            if (!System.IO.File.Exists(_credentialPath)) return;
            byte[] encrypted = System.IO.File.ReadAllBytes(_credentialPath);
            byte[] clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var saved = JsonSerializer.Deserialize<Credentials>(clear);
            _email = saved?.Email;
            _password = saved?.Password;
            CryptographicOperations.ZeroMemory(clear);
        }
        catch
        {
            _email = null;
            _password = null;
        }
    }

    private static string GetString(JsonElement fields, string name)
    {
        return fields.TryGetProperty(name, out var value) && value.TryGetProperty("stringValue", out var result)
            ? result.GetString() ?? "" : "";
    }

    // Eksik veya bozuk exitPassword null döner; bu durumda uygulama kapatılamaz (kilitli kalır).
    private static ExitPasswordHash? GetExitPassword(JsonElement fields)
    {
        if (!fields.TryGetProperty("exitPassword", out var field)
            || !field.TryGetProperty("mapValue", out var map)
            || !map.TryGetProperty("fields", out var hashFields))
            return null;

        string algo = GetString(hashFields, "algo");
        string salt = GetString(hashFields, "saltB64");
        string hash = GetString(hashFields, "hashB64");
        int iterations = GetInt(hashFields, "iterations", 0);
        if (string.IsNullOrWhiteSpace(algo) || string.IsNullOrWhiteSpace(salt) || string.IsNullOrWhiteSpace(hash))
            return null;

        return new ExitPasswordHash(algo, iterations, salt, hash);
    }

    private static int GetInt(JsonElement fields, string name, int fallback)
    {
        return GetNullableInt(fields, name) ?? fallback;
    }

    private static int? GetNullableInt(JsonElement fields, string name)
    {
        if (!fields.TryGetProperty(name, out var value)) return null;
        if (value.TryGetProperty("integerValue", out var integer) &&
            int.TryParse(integer.GetString(), out int result)) return result;
        if (value.TryGetProperty("doubleValue", out var dbl) && dbl.TryGetDouble(out double number)) return (int)number;
        return null;
    }

    private static bool? GetNullableBool(JsonElement fields, string name)
    {
        return fields.TryGetProperty(name, out var value) && value.TryGetProperty("booleanValue", out var result)
            ? result.GetBoolean() : null;
    }

    private sealed class Credentials
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
    }
}

internal sealed record FirebaseSnapshot(
    int BaseSeconds,
    int BonusSeconds,
    int? CloudRemainingSeconds,
    bool? CloudIsPaused,
    string DateKey,
    bool StateDocumentMissing,
    ExitPasswordHash? ExitPassword,
    bool AllowedWindowsEnabled,
    System.Collections.Generic.IReadOnlyList<AllowedWindow> AllowedWindows);
