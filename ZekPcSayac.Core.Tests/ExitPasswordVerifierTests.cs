using System.Security.Cryptography;
using System.Text;
using ZekPcSayac.Core;

namespace ZekPcSayac.Core.Tests;

public class ExitPasswordVerifierTests
{
    private static readonly string Salt16Of01 = Convert.ToBase64String(Enumerable.Repeat((byte)0x01, 16).ToArray());

    [Fact]
    public void Dogru_sifre_kabul_edilir()
    {
        var hash = ExitPasswordVerifier.Create("test1234", 10000);

        Assert.True(ExitPasswordVerifier.Verify("test1234", hash));
    }

    [Fact]
    public void Yanlis_sifre_reddedilir()
    {
        var hash = ExitPasswordVerifier.Create("test1234", 10000);

        Assert.False(ExitPasswordVerifier.Verify("test1235", hash));
        Assert.False(ExitPasswordVerifier.Verify("", hash));
    }

    [Fact]
    public void Bozuk_base64_false_doner()
    {
        var hash = new ExitPasswordHash("PBKDF2-SHA256", 10000, "!!!bozuk!!!", "!!!bozuk!!!");

        Assert.False(ExitPasswordVerifier.Verify("test1234", hash));
    }

    [Fact]
    public void Bozuk_hash_uzunlugu_false_doner()
    {
        var shortHash = new ExitPasswordHash("PBKDF2-SHA256", 10000, Salt16Of01, Convert.ToBase64String(new byte[8]));

        Assert.False(ExitPasswordVerifier.Verify("test1234", shortHash));
    }

    [Fact]
    public void Dusuk_iterasyon_false_doner()
    {
        var hash = ExitPasswordVerifier.Create("test1234", 10000);
        var lowIter = hash with { Iterations = 9999 };

        Assert.False(ExitPasswordVerifier.Verify("test1234", lowIter));
    }

    [Fact]
    public void Desteklenmeyen_algoritma_false_doner()
    {
        var hash = ExitPasswordVerifier.Create("test1234", 10000);

        Assert.False(ExitPasswordVerifier.Verify("test1234", hash with { Algo = "PBKDF2-SHA1" }));
    }

    [Fact]
    public void Create_ve_Verify_uyumludur_varsayilan_iterasyon()
    {
        var hash = ExitPasswordVerifier.Create("test1234");

        Assert.Equal(150000, hash.Iterations);
        Assert.Equal("PBKDF2-SHA256", hash.Algo);
        Assert.True(ExitPasswordVerifier.Verify("test1234", hash));
    }

    [Fact]
    public void Dogrulama_kayitli_base64_hash_ile_calisir()
    {
        // Firestore'dan gelen biçim: salt ve hash base64 metin olarak okunur.
        // Hash, bu testte bağımsız bir referans olmadığı için yalnızca round-trip olarak kontrol edilir.
        var created = ExitPasswordVerifier.Create("test1234", 10000);
        var restored = new ExitPasswordHash(created.Algo, created.Iterations, created.SaltB64, created.HashB64);

        Assert.True(ExitPasswordVerifier.Verify("test1234", restored));
        Assert.False(ExitPasswordVerifier.Verify("test1235", restored));
    }

    [Fact]
    public void PBKDF2_SHA256_standart_vektor_ile_eslesir()
    {
        // Bağımsız referans: PBKDF2-HMAC-SHA256, P="password", S="salt", c=4096, dkLen=32.
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2("password", Encoding.UTF8.GetBytes("salt"), 4096, HashAlgorithmName.SHA256, 32);

        Assert.Equal("c5e478d59288c841aa530db6845c4c8d962893a001ce4e11a4963873aa98134a", Convert.ToHexString(derived).ToLowerInvariant());
    }

    [Fact]
    public void Create_dusuk_iterasyonu_reddeder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExitPasswordVerifier.Create("test1234", 9999));
    }
}
