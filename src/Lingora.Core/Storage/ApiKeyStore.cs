using System.Security.Cryptography;
using System.Text;
using Lingora.Core.Models;

namespace Lingora.Core.Storage;

public enum ApiKeySource
{
    None,
    Settings,
    Environment,
}

/// <summary>API anahtarı arama sonucu.</summary>
public sealed record ApiKeyLookup(string? Key, ApiKeySource Source, string? Error = null)
{
    public bool HasKey => !string.IsNullOrEmpty(Key);
}

/// <summary>
/// API anahtarını ayarlar içinde şifreli saklar. Anahtar koda gömülmez;
/// Windows'ta DPAPI (yalnızca bu Windows kullanıcısı çözebilir) kullanılır.
/// </summary>
public sealed class ApiKeyStore
{
    public const string EnvironmentVariable = "GEMINI_API_KEY";

    private const string DpapiPrefix = "dpapi:";
    private const string PlainPrefix = "plain:";
    // Uygulamanın eski adıyla kalır: değiştirilirse önceki sürümde kaydedilen anahtarlar çözülemez.
    private static readonly byte[] Entropy = "CeviriPratik.ApiKey.v1"u8.ToArray();

    private readonly Func<string, string?> _readEnvironment;

    public ApiKeyStore(Func<string, string?>? readEnvironment = null)
    {
        _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
    }

    /// <summary>Anahtar biçimini denetler; sorun yoksa null, varsa Türkçe hata mesajı döner.</summary>
    public static string? Validate(string? apiKey)
    {
        var key = (apiKey ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return "API anahtarı boş olamaz.";
        }

        if (key.Any(char.IsWhiteSpace))
        {
            return "API anahtarı boşluk içeremez.";
        }

        return null;
    }

    public void Set(AppSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (Validate(apiKey) is { } error)
        {
            throw new ArgumentException(error, nameof(apiKey));
        }

        settings.ProtectedApiKey = Protect(apiKey.Trim());
    }

    public void Clear(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ProtectedApiKey = null;
    }

    /// <summary>Önce ayarlardaki şifreli anahtar, yoksa GEMINI_API_KEY ortam değişkeni.</summary>
    public ApiKeyLookup Get(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? error = null;

        if (!string.IsNullOrEmpty(settings.ProtectedApiKey))
        {
            try
            {
                return new ApiKeyLookup(Unprotect(settings.ProtectedApiKey), ApiKeySource.Settings);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or NotSupportedException)
            {
                error = "Kayıtlı API anahtarı çözülemedi (başka bir Windows kullanıcısı veya bilgisayarda kaydedilmiş olabilir). Lütfen anahtarı yeniden girin.";
            }
        }

        var fromEnvironment = _readEnvironment(EnvironmentVariable)?.Trim();
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            return new ApiKeyLookup(fromEnvironment, ApiKeySource.Environment, error);
        }

        return new ApiKeyLookup(null, ApiKeySource.None, error);
    }

    /// <summary>Ekranda göstermek için anahtarı maskeler: "AIza…x9Kq".</summary>
    public static string Mask(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "(yok)";
        }

        return key.Length <= 10 ? new string('*', key.Length) : $"{key[..4]}…{key[^4..]}";
    }

    internal static string Protect(string plain)
    {
        var bytes = Encoding.UTF8.GetBytes(plain);
        if (OperatingSystem.IsWindows())
        {
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(encrypted);
        }

        // Windows dışı sistemlerde DPAPI yok; en azından düz metin olarak görünmesin.
        return PlainPrefix + Convert.ToBase64String(bytes);
    }

    internal static string Unprotect(string stored)
    {
        if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new NotSupportedException("DPAPI yalnızca Windows'ta çalışır.");
            }

            var encrypted = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser));
        }

        if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal))
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(stored[PlainPrefix.Length..]));
        }

        throw new FormatException("Bilinmeyen anahtar biçimi.");
    }
}
