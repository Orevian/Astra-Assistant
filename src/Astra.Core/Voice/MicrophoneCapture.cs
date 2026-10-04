using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Astra.Core.Voice;

public sealed record MicDevice(string Id, string Name, bool IsDefault);

/// <summary>Microphone input at 16 kHz mono (what Whisper expects), with a selectable device.</summary>
public sealed class MicrophoneCapture : IDisposable
{
    private WaveInEvent? _wave;

    /// <summary>20 ms frames of mono 16 kHz samples in [-1, 1].</summary>
    public event Action<float[]>? Frame;
    public event Action<string>? Error;

    public bool IsRunning => _wave is not null;

    public static IReadOnlyList<MicDevice> Devices()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            string? def = null;
            try { def = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID; } catch { }
            return en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(d => new MicDevice(d.ID, d.FriendlyName, d.ID == def)).ToList();
        }
        catch { return Array.Empty<MicDevice>(); }
    }

    private static int WaveInIndexFor(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return -1; // WAVE_MAPPER: the Windows default input
        var dev = Devices().FirstOrDefault(d => d.Id == deviceId);
        if (dev is null) return -1;
        // WinMM truncates product names to 31 chars, so match by prefix.
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var name = WaveInEvent.GetCapabilities(i).ProductName;
            if (dev.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    public void Start(string? deviceId)
    {
        Stop();
        try
        {
            _wave = new WaveInEvent
            {
                DeviceNumber = WaveInIndexFor(deviceId),
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 20,
                NumberOfBuffers = 6,
            };
            _wave.DataAvailable += OnData;
            _wave.RecordingStopped += (_, e) => { if (e.Exception is not null) Error?.Invoke(e.Exception.Message); };
            _wave.StartRecording();
        }
        catch (Exception ex)
        {
            _wave?.Dispose();
            _wave = null;
            Error?.Invoke(ex.Message);
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var n = e.BytesRecorded / 2;
        if (n == 0) return;
        var frame = new float[n];
        for (var i = 0; i < n; i++) frame[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
        Frame?.Invoke(frame);
    }

    public void Stop()
    {
        if (_wave is null) return;
        try { _wave.DataAvailable -= OnData; _wave.StopRecording(); } catch { }
        _wave.Dispose();
        _wave = null;
    }

    public void Dispose() => Stop();
}
