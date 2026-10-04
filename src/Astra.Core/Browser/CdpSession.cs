using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Astra.Core.Browser;

public sealed record BrowserTab(string Id, string Title, string Url, bool Active);
public sealed record PageElement(int Index, string Tag, string Text, string? Href);
public sealed record PageSnapshot(string Title, string Url, string Text, List<PageElement> Elements);

/// <summary>Minimal Chrome DevTools Protocol client for Chromium-based browsers.</summary>
public sealed class CdpSession : IDisposable
{
    private readonly int _port;
    private readonly Process? _process;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private ClientWebSocket? _ws;
    private string? _tabId;
    private int _nextId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private CancellationTokenSource? _readerCts;

    private CdpSession(int port, Process? process) { _port = port; _process = process; }

    public bool IsAlive => _process is { HasExited: false } || _process is null;

    public static async Task<CdpSession> StartAsync(string exe, string profileDir, CancellationToken ct)
    {
        var port = FreePort();
        Directory.CreateDirectory(profileDir);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            ArgumentList =
            {
                $"--remote-debugging-port={port}", $"--user-data-dir={profileDir}", "--no-first-run", "--no-default-browser-check",
                "--remote-allow-origins=*", "about:blank",
            },
        };
        var proc = Process.Start(psi)!;
        var session = new CdpSession(port, proc);
        for (var i = 0; i < 60; i++)
        {
            ct.ThrowIfCancellationRequested();
            try { await Http.GetStringAsync($"http://127.0.0.1:{port}/json/version", ct); break; }
            catch { await Task.Delay(250, ct); }
            if (i == 59) { session.Dispose(); throw new InvalidOperationException("The browser did not open its DevTools endpoint."); }
        }
        await session.AttachFirstTabAsync(ct);
        return session;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // ---- Tabs --------------------------------------------------------------

    public async Task<List<BrowserTab>> TabsAsync(CancellationToken ct = default)
    {
        var json = await Http.GetStringAsync($"http://127.0.0.1:{_port}/json/list", ct);
        var arr = JsonNode.Parse(json)!.AsArray();
        return arr.Where(t => (string?)t?["type"] == "page")
            .Select(t => new BrowserTab((string)t!["id"]!, (string?)t["title"] ?? "", (string?)t["url"] ?? "", (string)t["id"]! == _tabId)).ToList();
    }

    private async Task AttachFirstTabAsync(CancellationToken ct)
    {
        var tabs = await TabsAsync(ct);
        await AttachAsync(tabs.First().Id, ct);
    }

    public async Task AttachAsync(string tabId, CancellationToken ct = default)
    {
        _readerCts?.Cancel();
        _ws?.Dispose();
        var json = await Http.GetStringAsync($"http://127.0.0.1:{_port}/json/list", ct);
        var tab = JsonNode.Parse(json)!.AsArray().First(t => (string?)t?["id"] == tabId)!;
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri((string)tab["webSocketDebuggerUrl"]!), ct);
        _tabId = tabId;
        _readerCts = new CancellationTokenSource();
        _ = ReadLoopAsync(_ws, _readerCts.Token);
        await SendAsync("Page.enable", null, ct);
        await SendAsync("Runtime.enable", null, ct);
        try { await Http.GetStringAsync($"http://127.0.0.1:{_port}/json/activate/{tabId}", ct); } catch { }
    }

    public async Task<string> NewTabAsync(string url, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"http://127.0.0.1:{_port}/json/new?{Uri.EscapeDataString(url)}");
        using var res = await Http.SendAsync(req, ct);
        var tab = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))!;
        var id = (string)tab["id"]!;
        await AttachAsync(id, ct);
        return id;
    }

    public async Task CloseTabAsync(string tabId, CancellationToken ct = default)
    {
        await Http.GetStringAsync($"http://127.0.0.1:{_port}/json/close/{tabId}", ct);
        if (tabId == _tabId)
        {
            var rest = await TabsAsync(ct);
            if (rest.Count > 0) await AttachAsync(rest[0].Id, ct);
        }
    }

    // ---- Protocol plumbing -------------------------------------------------

    private async Task ReadLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[1 << 16];
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buf, ct);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                } while (!r.EndOfMessage);
                var msg = JsonNode.Parse(sb.ToString());
                if (msg?["id"] is { } idNode && _pending.TryRemove((int)idNode, out var tcs))
                {
                    if (msg["error"] is { } err) tcs.TrySetException(new InvalidOperationException((string?)err["message"] ?? "DevTools error"));
                    else tcs.TrySetResult(msg["result"]);
                }
            }
        }
        catch { }
        finally
        {
            foreach (var p in _pending.Values) p.TrySetCanceled();
        }
    }

    public async Task<JsonNode?> SendAsync(string method, JsonObject? args, CancellationToken ct = default)
    {
        if (_ws is not { State: WebSocketState.Open }) throw new InvalidOperationException("DevTools connection is closed.");
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var payload = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = args ?? new JsonObject() };
        await _ws.SendAsync(Encoding.UTF8.GetBytes(payload.ToJsonString()), WebSocketMessageType.Text, true, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await using var reg = timeout.Token.Register(() => tcs.TrySetCanceled());
        return await tcs.Task;
    }

    public async Task<string?> EvalAsync(string expression, CancellationToken ct = default)
    {
        var r = await SendAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression, ["returnByValue"] = true, ["awaitPromise"] = true,
        }, ct);
        if (r?["exceptionDetails"] is { } ex) throw new InvalidOperationException((string?)ex["exception"]?["description"] ?? "Script error");
        var v = r?["result"]?["value"];
        return v switch { null => null, JsonValue jv when jv.TryGetValue<string>(out var s) => s, _ => v.ToJsonString() };
    }

    // ---- Page operations ---------------------------------------------------

    public async Task NavigateAsync(string url, CancellationToken ct = default)
    {
        await SendAsync("Page.navigate", new JsonObject { ["url"] = url }, ct);
        await WaitLoadedAsync(ct);
    }

    public async Task WaitLoadedAsync(CancellationToken ct, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        await Task.Delay(400, ct);
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try { if (await EvalAsync("document.readyState", ct) == "complete") return; } catch { }
            await Task.Delay(250, ct);
        }
    }

    private const string SnapshotScript = """
        (() => {
          const vis = e => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 2 && r.height > 2 && s.visibility !== 'hidden' && s.display !== 'none'; };
          let n = 0; const els = [];
          document.querySelectorAll('[data-astra-id]').forEach(e => e.removeAttribute('data-astra-id'));
          document.querySelectorAll('a[href], button, input, textarea, select, [role=button], [role=link], [onclick], summary').forEach(e => {
            if (n >= 45 || !vis(e)) return;
            n++; e.setAttribute('data-astra-id', n);
            const text = (e.innerText || e.value || e.getAttribute('aria-label') || e.placeholder || e.title || '').trim().replace(/\s+/g, ' ').slice(0, 80);
            els.push({ i: n, t: e.tagName.toLowerCase() + (e.type ? ':' + e.type : ''), x: text, h: e.href || null });
          });
          return JSON.stringify({ title: document.title, url: location.href, text: (document.body ? document.body.innerText : '').replace(/\n{3,}/g, '\n\n').slice(0, 4000), els });
        })()
        """;

    public async Task<PageSnapshot> SnapshotAsync(CancellationToken ct = default)
    {
        var json = await EvalAsync(SnapshotScript, ct) ?? "{}";
        var n = JsonNode.Parse(json)!;
        var els = n["els"]!.AsArray().Select(e => new PageElement((int)e!["i"]!, (string)e["t"]!, (string)e["x"]!, (string?)e["h"])).ToList();
        return new PageSnapshot((string)n["title"]!, (string)n["url"]!, (string)n["text"]!, els);
    }

    /// <summary>Clicks an element by snapshot index ("12"), CSS selector, or visible text, using a real mouse event at its center.</summary>
    public async Task<string> ClickAsync(string target, CancellationToken ct = default)
    {
        var t = JsonSerializer.Serialize(target);
        var js = $$"""
            (() => {
              const target = {{t}};
              let el = /^\d+$/.test(target) ? document.querySelector('[data-astra-id="' + target + '"]') : null;
              if (!el) { try { el = document.querySelector(target); } catch (e) {} }
              if (!el) { const low = target.toLowerCase(); el = [...document.querySelectorAll('a,button,[role=button],input[type=submit],summary')].find(e => (e.innerText || e.value || '').trim().toLowerCase() === low) || [...document.querySelectorAll('a,button,[role=button]')].find(e => (e.innerText || '').toLowerCase().includes(low)); }
              if (!el) return JSON.stringify({ ok: false });
              el.scrollIntoView({ block: 'center' });
              const r = el.getBoundingClientRect();
              return JSON.stringify({ ok: true, x: r.left + r.width / 2, y: r.top + r.height / 2, label: (el.innerText || el.value || el.tagName).slice(0, 60) });
            })()
            """;
        var res = JsonNode.Parse(await EvalAsync(js, ct) ?? "{}")!;
        if (res["ok"]?.GetValue<bool>() != true) return "";
        var x = res["x"]!.GetValue<double>();
        var y = res["y"]!.GetValue<double>();
        foreach (var (type, buttons) in new[] { ("mouseMoved", 0), ("mousePressed", 1), ("mouseReleased", 1) })
            await SendAsync("Input.dispatchMouseEvent", new JsonObject
            {
                ["type"] = type, ["x"] = x, ["y"] = y, ["button"] = type == "mouseMoved" ? "none" : "left", ["clickCount"] = 1, ["buttons"] = buttons,
            }, ct);
        await Task.Delay(500, ct);
        return (string?)res["label"] ?? target;
    }

    public async Task<bool> TypeAsync(string target, string text, bool submit, CancellationToken ct = default)
    {
        var t = JsonSerializer.Serialize(target);
        var focused = await EvalAsync($$"""
            (() => {
              const target = {{t}};
              let el = /^\d+$/.test(target) ? document.querySelector('[data-astra-id="' + target + '"]') : null;
              if (!el && target) { try { el = document.querySelector(target); } catch (e) {} }
              if (!el) el = document.querySelector('input[type=search],input[name=q],input[type=text],textarea,[contenteditable=true]');
              if (!el) return 'no';
              el.focus(); if (el.select) el.select(); return 'yes';
            })()
            """, ct);
        if (focused != "yes") return false;
        await SendAsync("Input.insertText", new JsonObject { ["text"] = text }, ct);
        if (submit)
        {
            foreach (var type in new[] { "keyDown", "keyUp" })
                await SendAsync("Input.dispatchKeyEvent", new JsonObject
                {
                    ["type"] = type, ["key"] = "Enter", ["code"] = "Enter", ["windowsVirtualKeyCode"] = 13, ["text"] = type == "keyDown" ? "\r" : null,
                }, ct);
            await WaitLoadedAsync(ct, 8000);
        }
        return true;
    }

    public async Task<byte[]> ScreenshotAsync(CancellationToken ct = default)
    {
        var r = await SendAsync("Page.captureScreenshot", new JsonObject { ["format"] = "png" }, ct);
        return Convert.FromBase64String((string)r!["data"]!);
    }

    public void Dispose()
    {
        _readerCts?.Cancel();
        try { _ws?.Dispose(); } catch { }
        try { if (_process is { HasExited: false }) _process.CloseMainWindow(); } catch { }
    }
}
