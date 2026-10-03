using System.Net.Http.Headers;
using System.Text;
using Astra.Core.Security;
using Astra.Core.Settings;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;

namespace Astra.Core.Voice;

public sealed record VoiceInfo(string Id, string Name, string Language, string Provider);

/// <summary>
/// Text-to-speech with a provider abstraction: Windows voices (local) or OpenAI speech (external).
/// Audio is played through NAudio so the exact samples being heard drive the real-time spectrum.
/// </summary>
public sealed class TtsService : IDisposable
{
    private readonly SettingsStore _settings;
    private WaveOutEvent? _out;
    private TaskCompletionSource? _done;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>~32 log-spaced frequency bands in 0..1, raised from the audio thread while speaking.</summary>
    public event Action<float[]>? Spectrum;
    public event Action<bool>? SpeakingChanged;

    public bool IsSpeaking { get; private set; }

    public TtsService(SettingsStore settings) => _settings = settings;

    public static readonly string[] OpenAiVoices = { "alloy", "echo", "fable", "onyx", "nova", "shimmer" };

    public static IReadOnlyList<VoiceInfo> Voices(string provider)
    {
        if (provider == "openai") return OpenAiVoices.Select(v => new VoiceInfo(v, v, "multi", "openai")).ToList();
        try
        {
            return SpeechSynthesizer.AllVoices.OrderBy(v => v.Language).ThenBy(v => v.DisplayName)
                .Select(v => new VoiceInfo(v.Id, v.DisplayName, v.Language, "windows")).ToList();
        }
        catch { return Array.Empty<VoiceInfo>(); }
    }

    /// <summary>The configured voice if it speaks the reply language, otherwise the best installed voice for it.</summary>
    private VoiceInformation PickWindowsVoice(string lang)
    {
        var all = SpeechSynthesizer.AllVoices;
        var configured = all.FirstOrDefault(v => v.Id == _settings.Current.Voice.TtsVoice);
        if (configured is not null && configured.Language.StartsWith(lang, StringComparison.OrdinalIgnoreCase)) return configured;
        return all.FirstOrDefault(v => v.Language.StartsWith(lang, StringComparison.OrdinalIgnoreCase)) ?? configured ?? SpeechSynthesizer.DefaultVoice;
    }

    /// <summary>Strips markdown and trims long answers so speech stays short and natural.</summary>
    public static string ForSpeech(string text)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(text, @"[`*_#>]|\[(.*?)\]\(.*?\)", "$1");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"[A-Za-z]:\\[^\s]+", "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length <= 280) return t;
        var cut = t.LastIndexOfAny(new[] { '.', '!', '?' }, 280);
        return cut > 80 ? t[..(cut + 1)] : t[..280];
    }

    public async Task SpeakAsync(string text, string lang, CancellationToken ct = default)
    {
        text = ForSpeech(text);
        if (text.Length == 0) return;
        Stop();
        byte[]? wav = null;
        try
        {
            wav = _settings.Current.Voice.TtsProvider == "openai" ? await SynthesizeOpenAiAsync(text, ct) : await SynthesizeWindowsAsync(text, lang);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            if (_settings.Current.Voice.TtsProvider == "openai") wav = await SynthesizeWindowsAsync(text, lang); // fall back to the local voice
        }
        if (wav is null) return;
        await PlayAsync(wav, ct);
    }

    private async Task<byte[]> SynthesizeWindowsAsync(string text, string lang)
    {
        using var synth = new SpeechSynthesizer();
        synth.Voice = PickWindowsVoice(lang);
        synth.Options.SpeakingRate = Math.Clamp(_settings.Current.Voice.TtsSpeed, 0.5, 2.0);
        using var stream = await synth.SynthesizeTextToStreamAsync(text);
        var bytes = new byte[stream.Size];
        using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private async Task<byte[]> SynthesizeOpenAiAsync(string text, CancellationToken ct)
    {
        var key = CredentialStore.Load(CredentialStore.ProviderKeyName("openai")) ?? throw new InvalidOperationException("No OpenAI key");
        var voice = OpenAiVoices.Contains(_settings.Current.Voice.TtsVoice ?? "") ? _settings.Current.Voice.TtsVoice : "nova";
        var body = new System.Text.Json.Nodes.JsonObject
        {
            ["model"] = "gpt-4o-mini-tts", ["voice"] = voice, ["input"] = text, ["response_format"] = "wav",
            ["speed"] = Math.Clamp(_settings.Current.Voice.TtsSpeed, 0.5, 2.0),
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var res = await Http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task PlayAsync(byte[] wav, CancellationToken ct)
    {
        var reader = new WaveFileReader(new MemoryStream(wav));
        var tap = new SpectrumTap(reader.ToSampleProvider(), bands => Spectrum?.Invoke(bands));
        var output = new WaveOutEvent();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, _) => done.TrySetResult();
        output.Init(tap);
        output.Volume = Math.Clamp(_settings.Current.Voice.TtsVolume / 100f, 0f, 1f);
        _out = output;
        _done = done;
        SetSpeaking(true);
        output.Play();
        try
        {
            using var reg = ct.Register(() => { try { output.Stop(); } catch { } });
            await done.Task;
        }
        finally
        {
            SetSpeaking(false);
            Spectrum?.Invoke(new float[SpectrumTap.Bands]);
            output.Dispose();
            reader.Dispose();
            if (ReferenceEquals(_out, output)) _out = null;
        }
        ct.ThrowIfCancellationRequested();
    }

    public void Stop()
    {
        try { _out?.Stop(); } catch { }
    }

    private void SetSpeaking(bool v)
    {
        IsSpeaking = v;
        SpeakingChanged?.Invoke(v);
    }

    public void Dispose() => Stop();
}

/// <summary>Pass-through sample provider that computes a smoothed log-band spectrum of what is actually being played.</summary>
internal sealed class SpectrumTap : ISampleProvider
{
    public const int Bands = 32;
    private const int N = 1024;
    private readonly ISampleProvider _src;
    private readonly Action<float[]> _onBands;
    private readonly float[] _ring = new float[N];
    private int _fill;
    private readonly float[] _smooth = new float[Bands];
    private readonly float[] _window = Enumerable.Range(0, N).Select(i => (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)))).ToArray();

    public SpectrumTap(ISampleProvider src, Action<float[]> onBands) { _src = src; _onBands = onBands; }
    public WaveFormat WaveFormat => _src.WaveFormat;

    public int Read(Span<float> buffer)
    {
        var read = _src.Read(buffer);
        var ch = WaveFormat.Channels;
        for (var i = 0; i + ch <= read; i += ch)
        {
            float mono = 0;
            for (var c = 0; c < ch; c++) mono += buffer[i + c];
            _ring[_fill++] = mono / ch;
            if (_fill == N) { Analyze(); _fill = N / 2; Array.Copy(_ring, N / 2, _ring, 0, N / 2); }
        }
        return read;
    }

    private void Analyze()
    {
        var re = new float[N];
        var im = new float[N];
        for (var i = 0; i < N; i++) re[i] = _ring[i] * _window[i];
        Fft(re, im);

        var rate = WaveFormat.SampleRate;
        var bands = new float[Bands];
        var fMin = 80.0; var fMax = Math.Min(8000.0, rate / 2.0);
        for (var b = 0; b < Bands; b++)
        {
            var lo = fMin * Math.Pow(fMax / fMin, b / (double)Bands);
            var hi = fMin * Math.Pow(fMax / fMin, (b + 1) / (double)Bands);
            var i0 = Math.Max(1, (int)(lo / rate * N));
            var i1 = Math.Max(i0 + 1, (int)(hi / rate * N));
            double sum = 0;
            for (var i = i0; i < i1 && i < N / 2; i++) sum += Math.Sqrt(re[i] * re[i] + im[i] * im[i]);
            var mag = sum / (i1 - i0);
            var db = 20 * Math.Log10(mag / 6 + 1e-6);          // ~ -60..0 dB
            var v = (float)Math.Clamp((db + 55) / 55, 0, 1);
            _smooth[b] = v > _smooth[b] ? v : _smooth[b] * 0.82f + v * 0.18f; // fast attack, soft release
            bands[b] = _smooth[b];
        }
        _onBands(bands);
    }

    private static void Fft(float[] re, float[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len;
            var wr = (float)Math.Cos(ang); var wi = (float)Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                float cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var ur = re[i + k]; var ui = im[i + k];
                    var vr = re[i + k + len / 2] * cr - im[i + k + len / 2] * ci;
                    var vi = re[i + k + len / 2] * ci + im[i + k + len / 2] * cr;
                    re[i + k] = ur + vr; im[i + k] = ui + vi;
                    re[i + k + len / 2] = ur - vr; im[i + k + len / 2] = ui - vi;
                    var nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
    }
}
