using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZekPcSayac.Core;

public enum LoadStatus
{
    /// <summary>Dosya yok (ilk çalıştırma).</summary>
    Missing,
    /// <summary>İmza doğrulandı (veya eski imzasız dosya kabul edildi).</summary>
    Ok,
    /// <summary>İmza uyuşmuyor, dosya bozuk veya geçersiz. Çağıran kilitli başlatmalı.</summary>
    Tampered
}

/// <summary>Yükleme sonucu. <see cref="State"/> yalnızca <see cref="LoadStatus.Ok"/> durumunda doludur.</summary>
public sealed record LoadResult(LoadStatus Status, UsageState? State = null);

/// <summary>
/// <see cref="UsageState"/> değerini HMAC-SHA256 ile imzalayarak saklar.
/// Dosya biçimi: {"payload":"&lt;UsageState json&gt;","mac":"&lt;base64 HMAC&gt;"}.
/// Anahtar çağıran tarafından sağlanır (WPF tarafında DPAPI ile saklanır).
/// </summary>
public sealed class SignedStateStore
{
    private readonly string _path;
    private readonly byte[] _key;

    public SignedStateStore(string path, byte[] key32)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(key32);
        if (key32.Length != 32)
            throw new ArgumentException("Anahtar 32 bayt olmalı.", nameof(key32));

        _path = path;
        _key = (byte[])key32.Clone();
    }

    /// <summary>Durumu imzalayıp geçici dosya üzerinden atomik olarak yazar.</summary>
    public void Save(UsageState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        string payload = JsonSerializer.Serialize(state);
        string mac = Convert.ToBase64String(ComputeMac(payload));
        string envelope = JsonSerializer.Serialize(new Envelope(payload, mac));

        string tempFile = _path + ".tmp";
        File.WriteAllText(tempFile, envelope);
        File.Move(tempFile, _path, true);
    }

    /// <summary>
    /// Dosyayı okur ve imzayı doğrular. Eski imzasız format bir kez kabul edilir
    /// ve hemen imzalı biçimde yeniden yazılır.
    /// </summary>
    public LoadResult Load()
    {
        if (!File.Exists(_path))
            return new LoadResult(LoadStatus.Missing);

        string text = File.ReadAllText(_path);

        Envelope? envelope = TryParseEnvelope(text);
        if (envelope is not null)
        {
            if (!MacMatches(envelope.Payload, envelope.Mac))
                return new LoadResult(LoadStatus.Tampered);

            UsageState? signed = Deserialize(envelope.Payload);
            return signed is null
                ? new LoadResult(LoadStatus.Tampered)
                : new LoadResult(LoadStatus.Ok, signed);
        }

        // Eski imzasız format: bir kez kabul et, imzalı olarak yeniden yaz.
        UsageState? legacy = Deserialize(text);
        if (legacy is null)
            return new LoadResult(LoadStatus.Tampered);

        try
        {
            Save(legacy);
        }
        catch (IOException)
        {
            // Yeniden yazılamazsa bir sonraki açılışta aynı eski dosya tekrar kabul edilir.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new LoadResult(LoadStatus.Ok, legacy);
    }

    private byte[] ComputeMac(string payload)
    {
        return HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
    }

    private bool MacMatches(string payload, string mac)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(mac);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(ComputeMac(payload), expected);
    }

    private static Envelope? TryParseEnvelope(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!root.TryGetProperty("payload", out JsonElement payload) || payload.ValueKind != JsonValueKind.String)
                return null;
            if (!root.TryGetProperty("mac", out JsonElement mac) || mac.ValueKind != JsonValueKind.String)
                return null;

            return new Envelope(payload.GetString()!, mac.GetString()!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UsageState? Deserialize(string json)
    {
        try
        {
            UsageState? state = JsonSerializer.Deserialize<UsageState>(json);
            return state is not null && state.RemainingSeconds >= 0 ? state : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Envelope(
        [property: JsonPropertyName("payload")] string Payload,
        [property: JsonPropertyName("mac")] string Mac);
}
