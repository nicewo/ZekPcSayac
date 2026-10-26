using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZekPcSayac;

internal sealed class FirebaseSyncService
{
    private const string ProjectId = "zekpcsayac";
    private const string ApiKey = "AIzaSyDyfpxmS8-nl2tQfuzHV8or8g-e7e7lx5M";
    private const string DeviceId = "zekeriyas-pc";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly string _credentialPath;
    private string? _email;
    private string? _password;
    private string? _uid;
    private string? _idToken;
    private DateTime _tokenExpiresAt = DateTime.MinValue;

    public FirebaseSyncService(string appDirectory)
    {
        _credentialPath = System.IO.Path.Combine(appDirectory, "firebase-login.bin");
        LoadCredentials();
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
        var config = await GetDocumentAsync($"deviceConfig/{DeviceId}");
        var fields = config.RootElement.GetProperty("fields");
        string deviceUid = GetString(fields, "deviceAuthUid");
        if (!string.Equals(deviceUid, _uid, StringComparison.Ordinal))
            throw new InvalidOperationException("Bu Firebase hesabı cihaz için tanımlı değil. Web panelindeki PC UID ayarını kontrol et.");

        int dailyMinutes = Math.Clamp(GetInt(fields, "dailyLimitMinutes", 120), 0, 1440);
        int bonusMinutes = Math.Clamp(GetInt(fields, "bonusMinutes", 0), 0, 1440);
        string bonusDateKey = GetString(fields, "bonusDateKey");
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (!string.Equals(bonusDateKey, today, StringComparison.Ordinal))
            bonusMinutes = 0;

        JsonDocument? state = null;
        try { state = await GetDocumentAsync($"deviceState/{DeviceId}"); }
        catch (HttpRequestException ex) when (ex.Message.Contains("404", StringComparison.Ordinal)) { }

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

        return new FirebaseSnapshot(dailyMinutes * 60, bonusMinutes * 60, remaining, paused, dateKey, state is null);
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
            throw new HttpRequestException($"Firestore okuma başarısız: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
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
    bool StateDocumentMissing);
