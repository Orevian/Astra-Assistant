using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astra.Core.Settings;

/// <summary>Persists <see cref="AstraSettings"/> as JSON in %AppData%\Astra. Secrets never go here.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();
    private CancellationTokenSource? _debounce;

    public AstraSettings Current { get; private set; }
    public event Action<AstraSettings>? Changed;

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Astra");

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(DataDirectory, "settings.json");
        Current = Load();
    }

    private AstraSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AstraSettings>(File.ReadAllText(_path), Json) ?? new();
        }
        catch
        {
            // Corrupt file: keep a copy for the user and start from defaults.
            try { File.Copy(_path, _path + ".corrupt", overwrite: true); } catch { }
        }
        return new AstraSettings();
    }

    /// <summary>Raises <see cref="Changed"/> immediately and writes to disk after a short debounce.</summary>
    public void Commit()
    {
        Changed?.Invoke(Current);
        CancellationTokenSource cts;
        lock (_gate)
        {
            _debounce?.Cancel();
            cts = _debounce = new CancellationTokenSource();
        }
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(300, cts.Token); SaveNow(); } catch (OperationCanceledException) { }
        });
    }

    public void SaveNow()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, _path, overwrite: true);
        }
    }

    public void Reset()
    {
        Current = new AstraSettings();
        Commit();
    }
}
