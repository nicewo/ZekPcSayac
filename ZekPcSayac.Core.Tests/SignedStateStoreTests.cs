using System.Text.Json;
using ZekPcSayac.Core;

namespace ZekPcSayac.Core.Tests;

public class SignedStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "zek-state-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly byte[] _key = Enumerable.Repeat((byte)0x42, 32).ToArray();

    public SignedStateStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "usage-state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static UsageState SampleState() => new()
    {
        BaseSeconds = 7200,
        BonusSeconds = 600,
        RemainingSeconds = 3000,
        IsPaused = false,
        LastStartedUtc = new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc),
        DateKey = "2026-11-16",
        LastAppliedRemainingCommandId = "cmd-1",
        AudioWasMutedBeforePause = true,
        AudioWasMutedByApp = false
    };

    [Fact]
    public void Kaydet_ve_oku_ayni_degerleri_verir()
    {
        var store = new SignedStateStore(_path, _key);
        store.Save(SampleState());

        LoadResult result = store.Load();

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.NotNull(result.State);
        Assert.Equal(7200, result.State.BaseSeconds);
        Assert.Equal(600, result.State.BonusSeconds);
        Assert.Equal(3000, result.State.RemainingSeconds);
        Assert.False(result.State.IsPaused);
        Assert.Equal(new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc), result.State.LastStartedUtc);
        Assert.Equal("2026-11-16", result.State.DateKey);
        Assert.Equal("cmd-1", result.State.LastAppliedRemainingCommandId);
        Assert.True(result.State.AudioWasMutedBeforePause);
        Assert.False(result.State.AudioWasMutedByApp);
    }

    [Fact]
    public void Dosya_yoksa_Missing_doner()
    {
        var store = new SignedStateStore(_path, _key);

        LoadResult result = store.Load();

        Assert.Equal(LoadStatus.Missing, result.Status);
        Assert.Null(result.State);
    }

    [Fact]
    public void Payload_degisirse_Tampered_doner()
    {
        var store = new SignedStateStore(_path, _key);
        store.Save(SampleState());

        string original;
        string mac;
        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(_path)))
        {
            original = doc.RootElement.GetProperty("payload").GetString()!;
            mac = doc.RootElement.GetProperty("mac").GetString()!;
        }

        string tampered = original.Replace("\"RemainingSeconds\":3000", "\"RemainingSeconds\":99999");
        Assert.NotEqual(original, tampered);
        File.WriteAllText(_path, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["payload"] = tampered,
            ["mac"] = mac
        }));

        LoadResult result = store.Load();

        Assert.Equal(LoadStatus.Tampered, result.Status);
        Assert.Null(result.State);
    }

    [Fact]
    public void Farkli_anahtarla_okunursa_Tampered_doner()
    {
        new SignedStateStore(_path, _key).Save(SampleState());
        var otherKeyStore = new SignedStateStore(_path, Enumerable.Repeat((byte)0x11, 32).ToArray());

        LoadResult result = otherKeyStore.Load();

        Assert.Equal(LoadStatus.Tampered, result.Status);
    }

    [Fact]
    public void Imzasiz_eski_dosya_bir_kez_Ok_kabul_edilir_ve_imzali_yazilir()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(SampleState()));
        var store = new SignedStateStore(_path, _key);

        LoadResult first = store.Load();

        Assert.Equal(LoadStatus.Ok, first.Status);
        Assert.Equal(3000, first.State!.RemainingSeconds);

        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(_path)))
        {
            Assert.True(doc.RootElement.TryGetProperty("mac", out _));
            Assert.True(doc.RootElement.TryGetProperty("payload", out _));
        }

        LoadResult second = store.Load();
        Assert.Equal(LoadStatus.Ok, second.Status);
        Assert.Equal(3000, second.State!.RemainingSeconds);
    }

    [Fact]
    public void Imzasiz_dosya_yeniden_yazildiktan_sonra_farkli_anahtarla_Tampered_doner()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(SampleState()));
        new SignedStateStore(_path, _key).Load();

        LoadResult result = new SignedStateStore(_path, Enumerable.Repeat((byte)0x11, 32).ToArray()).Load();

        Assert.Equal(LoadStatus.Tampered, result.Status);
    }

    [Fact]
    public void Bozuk_dosya_Tampered_doner()
    {
        File.WriteAllText(_path, "{bozuk");

        LoadResult result = new SignedStateStore(_path, _key).Load();

        Assert.Equal(LoadStatus.Tampered, result.Status);
    }

    [Fact]
    public void Kisa_anahtar_reddedilir()
    {
        Assert.Throws<ArgumentException>(() => new SignedStateStore(_path, new byte[16]));
    }
}
