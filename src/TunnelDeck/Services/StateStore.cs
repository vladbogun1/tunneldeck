using System.IO;
using System.Text.Json;
using TunnelDeck.Models;

namespace TunnelDeck.Services;

/// <summary>Loads and saves <see cref="AppState"/> as JSON. Save is debounced-free but atomic.</summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();

    public AppState Load()
    {
        try
        {
            if (File.Exists(Paths.StateFile))
            {
                var json = File.ReadAllText(Paths.StateFile);
                var state = JsonSerializer.Deserialize<AppState>(json, Options);
                if (state is not null)
                    return state;
            }
        }
        catch
        {
            // Corrupt state file: start clean rather than crash.
        }
        return new AppState();
    }

    public void Save(AppState state)
    {
        lock (_gate)
        {
            Paths.EnsureDirs();
            var json = JsonSerializer.Serialize(state, Options);
            var tmp = Paths.StateFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Copy(tmp, Paths.StateFile, overwrite: true);
            File.Delete(tmp);
        }
    }
}
