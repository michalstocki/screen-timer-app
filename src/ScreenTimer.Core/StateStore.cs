using System.Text.Json;

namespace ScreenTimer.Core;

public static class StateStore
{
    public static string StatePath => Path.Combine(ConfigStore.Directory, "state.json");

    public static List<AccountState> Load(string? path = null)
    {
        path ??= StatePath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<List<AccountState>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt state: start clean rather than refusing to run.
        }
        return [];
    }

    public static void Save(IEnumerable<AccountState> states, string? path = null)
    {
        path ??= StatePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(states));
        File.Move(tmp, path, overwrite: true);
    }
}
