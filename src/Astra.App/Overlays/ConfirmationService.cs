using Astra.Core.Index;
using Astra.Core.Tools;
using Microsoft.UI.Dispatching;

namespace Astra.App.Overlays;

/// <summary>Shows the permission prompt on the UI thread. Non-destructive prompts can also be answered by voice ("yes" / "no").</summary>
public sealed class ConfirmationService : IConfirmationService
{
    private readonly DispatcherQueue _ui;
    private readonly SemaphoreSlim _one = new(1, 1);
    private ConfirmWindow? _open;

    private static readonly HashSet<string> Yes = new() { "evet", "tamam", "olur", "izin ver", "izin veriyorum", "yes", "yeah", "yep", "ok", "okay", "allow", "sure", "go ahead", "kabul" };
    private static readonly HashSet<string> No = new() { "hayir", "iptal", "yapma", "dur", "no", "nope", "cancel", "stop", "deny", "izin verme", "vazgec" };

    public ConfirmationService(DispatcherQueue ui) => _ui = ui;

    public bool IsWaiting => _open is not null;

    public async Task<bool> ConfirmAsync(ConfirmRequest request, CancellationToken ct)
    {
        await _one.WaitAsync(ct);
        try
        {
            var tcs = new TaskCompletionSource<ConfirmWindow>();
            _ui.TryEnqueue(() =>
            {
                var w = new ConfirmWindow(request);
                _open = w;
                w.Present();
                tcs.SetResult(w);
            });
            var window = await tcs.Task;
            using var reg = ct.Register(() => _ui.TryEnqueue(() => window.Decide(false)));
            var allowed = await window.Result;
            _open = null;
            return allowed;
        }
        finally { _one.Release(); }
    }

    /// <summary>If a prompt is open and the phrase is a clear yes/no, answers it and returns true.</summary>
    public bool TryAnswerByVoice(string text)
    {
        var w = _open;
        if (w is null) return false;
        var norm = TextNorm.Normalize(text);
        var answer = Yes.Contains(norm) ? true : No.Contains(norm) ? false : (bool?)null;
        if (answer is null) return false;
        // Irreversible actions need a click; a misheard "evet" must never delete files.
        if (answer == true && w.IsDestructive) return false;
        _ui.TryEnqueue(() => w.Decide(answer.Value));
        return true;
    }
}
