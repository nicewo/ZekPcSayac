using System.Security.Cryptography;
using System.Text;

namespace ZekPcSayac.Core;

/// <summary>Firestore'daki <c>deviceConfig.exitPassword</c> alanının karşılığı. Düz metin şifre tutulmaz.</summary>
public record ExitPasswordHash(string Algo, int Iterations, string SaltB64, string HashB64);

/// <summary>PBKDF2-SHA256 ile çıkış şifresi doğrulama. Web panel (WebCrypto) ile aynı parametreleri kullanır.</summary>
public static class ExitPasswordVerifier
{
    public const string SupportedAlgo = "PBKDF2-SHA256";
    public const int DefaultIterations = 150000;
    public const int MinimumIterations = 10000;
    private const int SaltLength = 16;
    private const int HashLength = 32;

    /// <summary>Verilen şifreyi hash ile karşılaştırır. Bozuk veya desteklenmeyen kayıtta her zaman false döner.</summary>
    public static bool Verify(string password, ExitPasswordHash hash)
    {
        if (password is null || hash is null) return false;
        if (hash.Algo != SupportedAlgo) return false;
        if (hash.Iterations < MinimumIterations) return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(hash.SaltB64 ?? "");
            expected = Convert.FromBase64String(hash.HashB64 ?? "");
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length != HashLength) return false;

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, hash.Iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Yeni rastgele salt ile hash üretir. Yalnızca test ve kurulum araçları için; uygulama şifre yazmaz.</summary>
    public static ExitPasswordHash Create(string password, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (iterations < MinimumIterations)
            throw new ArgumentOutOfRangeException(nameof(iterations), "İterasyon sayısı en az 10000 olmalı.");

        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashLength);
        return new ExitPasswordHash(SupportedAlgo, iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }
}
