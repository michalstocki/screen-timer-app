using System.Security.Cryptography;
using System.Text.Json;

namespace ScreenTimer.Core;

/// <summary>Administrator-controlled settings, stored in %ProgramData%\ScreenTimer\config.json.</summary>
public sealed class AppConfig
{
    // Fractions are accepted so tests and demos can run on a seconds scale.
    public double SessionMinutes { get; set; } = 30;
    public double BreakMinutes { get; set; } = 30;
    public double WarningMinutes { get; set; } = 2;
    public int[] ExtensionMinutes { get; set; } = [5, 10, 15];

    /// <summary>Accounts (DOMAIN\user, user or SID) that are never limited.</summary>
    public string[] ExemptAccounts { get; set; } = [];

    public string? CodeSalt { get; set; }
    public string? CodeHash { get; set; }
    public int CodeIterations { get; set; } = 200_000;

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

    public bool IsExempt(string? account, string? sid)
    {
        foreach (var entry in ExemptAccounts)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var e = entry.Trim();
            if (sid is not null && e.Equals(sid, StringComparison.OrdinalIgnoreCase)) return true;
            if (account is null) continue;
            if (e.Equals(account, StringComparison.OrdinalIgnoreCase)) return true;
            var slash = account.IndexOf('\\');
            if (slash >= 0 && e.Equals(account[(slash + 1)..], StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Clamps values so a hand-edited file cannot produce a nonsensical timer.</summary>
    public void Normalize()
    {
        SessionMinutes = Math.Clamp(SessionMinutes, 0.05, 24 * 60);
        BreakMinutes = Math.Clamp(BreakMinutes, 0.05, 24 * 60);
        WarningMinutes = Math.Clamp(WarningMinutes, 0, SessionMinutes);
        ExtensionMinutes = ExtensionMinutes.Where(m => m is > 0 and <= 240).Distinct().Order().ToArray();
        ExemptAccounts ??= [];
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
