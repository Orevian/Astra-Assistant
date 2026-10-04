using System.Text;
using Astra.Core.Index;
using Whisper.net;

namespace Astra.Core.Voice;

public sealed record WhisperModelInfo(string Id, string Name, int SizeMb, string Note);

/// <summary>Downloads and locates the local Whisper models (ggml format).</summary>
public static class WhisperModels
{
    public static readonly IReadOnlyList<WhisperModelInfo> Available = new[]
    {
        new WhisperModelInfo("tiny", "Tiny", 75, "Fastest, lowest accuracy"),
        new WhisperModelInfo("base", "Base", 142, "Balanced"),
        new WhisperModelInfo("small", "Small", 466, "Best accuracy, recommended (default)"),
    };

    public static string Directory_ { get; } = Path.Combine(Settings.SettingsStore.DataDirectory, "models");
    public static string PathFor(string id) => Path.Combine(Directory_, $"ggml-{id}.bin");
    public static bool IsInstalled(string id) => File.Exists(PathFor(id)) && new FileInfo(PathFor(id)).Length > 10_000_000;

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task DownloadAsync(string id, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Directory_);
        var url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{id}.bin";
        var part = PathFor(id) + ".part";
        using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength ?? 0;
        await using (var src = await res.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(part))
        {
            var buf = new byte[1 << 17];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress?.Report((double)read / total);
            }
        }
        File.Move(part, PathFor(id), overwrite: true);
        progress?.Report(1);
    }
}

public sealed record Transcript(string Text, string Language);

/// <summary>Local speech-to-text with whisper.cpp. Nothing leaves the PC.</summary>
public sealed class WhisperService : IDisposable
{
    private readonly Settings.SettingsStore _settings;
    private WhisperFactory? _factory;
    private string? _loadedModel;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WhisperService(Settings.SettingsStore settings) => _settings = settings;

    public string ModelId => _settings.Current.Voice.WhisperModel;
    public bool IsReady => WhisperModels.IsInstalled(ModelId);

    // Whisper invents these on silence/noise. Short ones must match exactly ("you" must not reject "YouTube").
    private static readonly HashSet<string> HallucinationExact = new()
    {
        "you", "bye", "bye.", "thank you.", "thanks.", "thank you", "tamam.", "hmm.", "uh", "um", ".", "...", "[music]", "(music)", "♪", "[blank_audio]",
    };
    private static readonly string[] HallucinationContains =
    {
        "thanks for watching", "thank you for watching", "subtitles by", "altyazı m.k.", "altyazi m.k.", "izlediğiniz için teşekkürler",
        "abone olmayı unutmayın", "amara.org",
    };

    /// <summary>Names Whisper should expect (wake word, browsers, games…). Set by the runtime from the local index.</summary>
    public Func<IEnumerable<string>>? Vocabulary { get; set; }

    public async Task<Transcript?> TranscribeAsync(float[] samples, string languageSetting, CancellationToken ct)
    {
        if (!IsReady) throw new InvalidOperationException("The speech model is not downloaded yet.");
        await _gate.WaitAsync(ct);
        try
        {
            if (_factory is null || _loadedModel != ModelId)
            {
                foreach (var p in _processors.Values) p.Dispose();
                _processors.Clear();
                _processorPrompt = null;
                _factory?.Dispose();
                _factory = WhisperFactory.FromPath(WhisperModels.PathFor(ModelId));
                _loadedModel = ModelId;
            }
            var lang = languageSetting switch { "tr" or "tr-TR" => "tr", "en" or "en-US" => "en", _ => "auto" };
            var result = await RunAsync(samples, lang, ct);
            if (result is null) return null;

            // Auto-detect on a short phrase can land on an unrelated language; Astra serves Turkish and English.
            if (lang == "auto" && result.Language is not ("tr" or "en"))
            {
                var fallback = Localization.Loc.IsTurkish ? "tr" : "en";
                result = await RunAsync(samples, fallback, ct);
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    private string BuildPrompt()
    {
        var words = new List<string> { _settings.Current.Voice.WakeWord };
        try { if (Vocabulary is not null) words.AddRange(Vocabulary().Take(60)); } catch { }
        var prompt = string.Join(", ", words.Distinct(StringComparer.OrdinalIgnoreCase));
        return (prompt.Length > 480 ? prompt[..480] : prompt) + ".";
    }

    private readonly Dictionary<string, WhisperProcessor> _processors = new();
    private string? _processorPrompt;

    private WhisperProcessor ProcessorFor(string lang)
    {
        var prompt = BuildPrompt();
        if (_processorPrompt != prompt)
        {
            foreach (var p in _processors.Values) p.Dispose();
            _processors.Clear();
            _processorPrompt = prompt;
        }
        if (!_processors.TryGetValue(lang, out var proc))
        {
            proc = _factory!.CreateBuilder()
                .WithLanguage(lang)
                .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 2, 8))
                .WithPrompt(prompt)
                .WithNoSpeechThreshold(0.6f)
                .Build();
            _processors[lang] = proc;
        }
        return proc;
    }

    /// <summary>Loads the model and runs a dummy pass so the first real command isn't slowed by GPU/shader initialisation.</summary>
    public async Task WarmUpAsync(CancellationToken ct)
    {
        if (!IsReady) return;
        try { await TranscribeAsync(new float[16000], "auto", ct); } catch { }
    }

    private async Task<Transcript?> RunAsync(float[] samples, string lang, CancellationToken ct)
    {
        var processor = ProcessorFor(lang);

        var sb = new StringBuilder();
        var detected = lang;
        await foreach (var seg in processor.ProcessAsync(samples, ct))
        {
            if (seg.NoSpeechProbability > 0.65f) continue;
            sb.Append(seg.Text);
            if (lang == "auto" && !string.IsNullOrEmpty(seg.Language)) detected = seg.Language;
        }
        var text = sb.ToString().Trim();
        if (text.Length < 2) return null;
        var low = text.ToLowerInvariant().Trim();
        if (HallucinationExact.Contains(low) || HallucinationContains.Any(low.Contains)) return null;
        return new Transcript(text, detected == "auto" ? (Localization.Loc.IsTurkish ? "tr" : "en") : detected);
    }

    public void Dispose()
    {
        foreach (var p in _processors.Values) p.Dispose();
        _factory?.Dispose();
        _gate.Dispose();
    }
}
