using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenTimer.Core;

/// <summary>A Windows account chosen in the settings. The SID is the identity; the name is for people.</summary>
public sealed class AccountRef
{
    public string Sid { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Administrator-controlled settings, stored in %ProgramData%\ScreenTimer\config.json.</summary>
public sealed class AppConfig
{
    // Fractions are accepted so tests and demos can run on a seconds scale.
    public double SessionMinutes { get; set; } = 30;
    public double BreakMinutes { get; set; } = 30;
    public double WarningMinutes { get; set; } = 2;
    public int[] ExtensionMinutes { get; set; } = [5, 10, 15];

    /// <summary>
    /// The accounts the app runs for and whose session time it counts. Empty means nobody is limited,
    /// so a fresh install cannot lock out an account the administrator has not chosen.
    /// </summary>
    public AccountRef[] LimitedAccounts { get; set; } = [];

    public string? CodeSalt { get; set; }
    public string? CodeHash { get; set; }
    public int CodeIterations { get; set; } = 200_000;

    [JsonIgnore]
    public bool HasCode => !string.IsNullOrEmpty(CodeHash) && !string.IsNullOrEmpty(CodeSalt);

    public const int MinCodeLength = 4;

    public void SetCode(string code)
    {
        if (code.Length < MinCodeLength)
            throw new ArgumentException($"Kod musi mieć co najmniej {MinCodeLength} znaki.", nameof(code));

        var salt = RandomNumberGenerator.GetBytes(16);
        CodeIterations = 200_000;
        CodeSalt = Convert.ToBase64String(salt);
        CodeHash = Convert.ToBase64String(Derive(code, salt, CodeIterations));
    }

    public bool VerifyCode(string? code)
    {
        if (!HasCode || code is null) return false;
        var actual = Derive(code, Convert.FromBase64String(CodeSalt!), CodeIterations);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(CodeHash!));
    }

    public bool IsLimited(string? sid) =>
        sid is not null && LimitedAccounts.Any(a => string.Equals(a.Sid, sid, StringComparison.OrdinalIgnoreCase));

    /// <summary>Clamps values so a hand-edited file cannot produce a nonsensical timer.</summary>
    public void Normalize()
    {
        SessionMinutes = Math.Clamp(SessionMinutes, 0.05, 24 * 60);
        BreakMinutes = Math.Clamp(BreakMinutes, 0.05, 24 * 60);
        WarningMinutes = Math.Clamp(WarningMinutes, 0, SessionMinutes);
        ExtensionMinutes = ExtensionMinutes.Where(m => m is > 0 and <= 240).Distinct().Order().ToArray();
        LimitedAccounts = (LimitedAccounts ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a?.Sid))
            .DistinctBy(a => a.Sid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CodeIterations = Math.Max(CodeIterations, 10_000);
    }

    static byte[] Derive(string code, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(code, salt, iterations, HashAlgorithmName.SHA256, 32);
}

public static class ConfigStore
{
    public static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ScreenTimer");

    public static string ConfigPath => Path.Combine(Directory, "config.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppConfig Load(string? path = null)
    {
        path ??= ConfigPath;
        try
        {
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig();
                cfg.Normalize();
                return cfg;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable config: fall back to defaults (no code set, so extensions stay disabled).
        }
        return new AppConfig();
    }

    public static void Save(AppConfig cfg, string? path = null)
    {
        path ??= ConfigPath;
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
