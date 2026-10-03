using System.Text.Json.Nodes;
using Astra.Core.Agent;
using Astra.Core.Assistant;
using Astra.Core.Browser;
using Astra.Core.Control;
using Astra.Core.Index;
using Astra.Core.Localization;
using Astra.Core.Memory;
using Astra.Core.Settings;
using Astra.Core.Tools;
using Astra.Core.Voice;
using Xunit;

namespace Astra.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; }

    /// <param name="outsideAppData">The indexer deliberately skips files under AppData\Local, so index tests need a folder elsewhere.</param>
    public TempDir(bool outsideAppData = false)
    {
        var baseDir = outsideAppData ? Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments) : System.IO.Path.GetTempPath();
        Path = System.IO.Path.Combine(baseDir, "astra-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public class TextNormTests
{
    [Theory]
    [InlineData("Çalıştır İstanbul ŞĞÜÖ", "calistir istanbul sguo")]
    [InlineData("Avast  Secure-Browser!", "avast secure browser")]
    [InlineData("", "")]
    public void Normalizes_turkish_and_punctuation(string input, string expected) => Assert.Equal(expected, TextNorm.Normalize(input));

    [Fact]
    public void Fts_query_is_prefix_and()
    {
        Assert.Equal("\"minecraft\"* \"mod\"*", TextNorm.ToFtsQuery("Minecraft Mod"));
        Assert.Equal("", TextNorm.ToFtsQuery("   "));
    }

    [Fact]
    public void Similarity_ranks_exact_over_prefix_over_fuzzy()
    {
        var exact = TextNorm.Similarity("chrome", "chrome");
        var prefix = TextNorm.Similarity("chro", "chrome");
        var typo = TextNorm.Similarity("chorme", "chrome");
        var unrelated = TextNorm.Similarity("valorant", "chrome");
        Assert.True(exact > prefix && prefix > typo && typo > unrelated);
    }

    [Fact]
    public void Turkish_suffix_still_matches_the_app_name() =>
        Assert.True(TextNorm.Similarity("hesap makinesini", "hesap makinesi") >= 0.85);
}

public class LanguageDetectorTests
{
    [Theory]
    [InlineData("Chrome'u aç ve YouTube'a gir", "tr")]
    [InlineData("open chrome and close discord", "en")]
    [InlineData("kırmızı butona tıkla", "tr")]
    public void Detects_language(string text, string expected) => Assert.Equal(expected, LanguageDetector.Detect(text));
}

public class HotkeyParsingTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", 3)]
    [InlineData("F9", 1)]
    [InlineData("win+shift+a", 3)]
    public void Parses_valid_chords(string chord, int count)
    {
        Assert.True(InputController.TryParseChord(chord, out var keys, out _));
        Assert.Equal(count, keys.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ctrl+banana")]
    public void Rejects_invalid_chords(string chord) => Assert.False(InputController.TryParseChord(chord, out _, out _));
}

public class BrowserUrlTests
{
    [Theory]
    [InlineData("youtube", "https://www.youtube.com")]
    [InlineData("github.com/anthropics", "https://github.com/anthropics")]
    [InlineData("https://example.com/x", "https://example.com/x")]
    public void Normalizes_urls(string input, string expected) => Assert.Equal(expected, BrowserController.NormalizeUrl(input));

    [Fact]
    public void Free_text_becomes_a_search()
    {
        var url = BrowserController.NormalizeUrl("best pizza in izmir");
        Assert.StartsWith("https://www.google.com/search?q=", url);
        Assert.Contains("pizza", url);
    }

    [Fact]
    public void Youtube_search_url() =>
        Assert.Equal("https://www.youtube.com/results?search_query=Maroon%205%20Misery", BrowserController.SearchUrl("Maroon 5 Misery", "youtube"));
}

public class MemoryStoreTests
{
    private static MemoryStore Create(TempDir t, bool enabled = true)
    {
        var settings = new SettingsStore(t.File("settings.json"));
        settings.Current.Memory.Enabled = enabled;
        return new MemoryStore(settings, t.File("memory.db"));
    }

    [Theory]
    [InlineData("my password is hunter2")]
    [InlineData("şifrem 123456")]
    [InlineData("card 4111 1111 1111 1111")]
    [InlineData("api key sk-abcdefghijklmnopqrstuvwxyz123456")]
    public void Refuses_sensitive_content(string text)
    {
        using var t = new TempDir();
        var (ok, _) = Create(t).Add(text);
        Assert.False(ok);
    }

    [Fact]
    public void Saves_finds_and_forgets()
    {
        using var t = new TempDir();
        var m = Create(t);
        Assert.True(m.Add("User prefers Avast Secure Browser").Ok);
        Assert.Contains("Avast", m.Relevant("open avast browser").Single());
        Assert.Equal(1, m.Forget("avast"));
        Assert.Empty(m.All());
    }

    [Fact]
    public void Disabled_memory_saves_nothing()
    {
        using var t = new TempDir();
        var m = Create(t, enabled: false);
        Assert.False(m.Add("likes jazz").Ok);
        Assert.Empty(m.Relevant("jazz"));
    }
}

public class SettingsTests
{
    [Fact]
    public void Defaults_match_the_product_spec()
    {
        var s = new AstraSettings();
        Assert.False(s.Appearance.SpeechBubbleEnabled);
        Assert.False(s.Appearance.VisualizerEnabled);
        Assert.Equal(ScreenCorner.TopRight, s.Appearance.VisualizerPosition);
        Assert.Equal("", s.Appearance.HudDisplay);          // empty = primary display
        Assert.Equal("", s.Appearance.NotificationDisplay);
        Assert.Equal("app", s.Appearance.IconId);           // icon.ico
        Assert.Equal("Astra", s.Voice.WakeWord);
    }

    [Fact]
    public void Round_trips_through_disk()
    {
        using var t = new TempDir();
        var a = new SettingsStore(t.File("s.json"));
        a.Current.Appearance.HudDisplay = @"\\.\DISPLAY2";
        a.Current.Appearance.Visualizer = VisualizerStyle.Orb;
        a.Current.Security.Permissions["files"] = PermissionLevel.Deny;
        a.SaveNow();
        var b = new SettingsStore(t.File("s.json"));
        Assert.Equal(@"\\.\DISPLAY2", b.Current.Appearance.HudDisplay);
        Assert.Equal(VisualizerStyle.Orb, b.Current.Appearance.Visualizer);
        Assert.Equal(PermissionLevel.Deny, b.Current.Security.Permissions["files"]);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults()
    {
        using var t = new TempDir();
        File.WriteAllText(t.File("s.json"), "{ not json");
        Assert.Equal("Astra", new SettingsStore(t.File("s.json")).Current.Voice.WakeWord);
    }
}

public class LocalizationTests
{
    [Fact]
    public void Round_trips_both_directions()
    {
        try
        {
            Loc.Set("tr");
            Assert.Equal("Ayarlar", Loc.T("Settings"));
            Loc.Set("en");
            Assert.Equal("Settings", Loc.T("Ayarlar"));
        }
        finally { Loc.Set("en"); }
    }

    [Fact]
    public void Format_placeholders_survive_translation()
    {
        // A translation that drops or adds a {n} placeholder would throw FormatException at runtime.
        var map = typeof(Loc).Assembly.GetType("Astra.Core.Localization.TurkishStrings")!;
        foreach (var name in new[] { "TurkishStrings", "TurkishStringsExtra" })
        {
            var dict = (Dictionary<string, string>)typeof(Loc).Assembly.GetType("Astra.Core.Localization." + name)!
                .GetField("Map")!.GetValue(null)!;
            foreach (var (en, tr) in dict)
                Assert.True(Placeholders(en).SetEquals(Placeholders(tr)), $"Placeholder mismatch: \"{en}\" vs \"{tr}\"");
        }
        _ = map;
    }

    private static HashSet<string> Placeholders(string s) =>
        System.Text.RegularExpressions.Regex.Matches(s, @"\{\d+(:[^}]*)?\}").Select(m => m.Value).ToHashSet();
}

public class WakeWordTests
{
    private static VoiceEngine Create(TempDir t, string wake = "Astra")
    {
        var settings = new SettingsStore(t.File("s.json"));
        settings.Current.Voice.WakeWord = wake;
        return new VoiceEngine(settings, new WhisperService(settings));
    }

    [Theory]
    [InlineData("Astra, open Chrome", true, "open Chrome")]
    [InlineData("Astro, Chrome'u aç.", true, "Chrome'u aç.")]
    [InlineData("hey astra close discord", true, "close discord")]
    [InlineData("Astra", true, "")]
    [InlineData("open Chrome please", false, null)]
    [InlineData("the astronaut landed", false, null)]
    public void Splits_wake_word(string text, bool heard, string? command)
    {
        using var t = new TempDir();
        var (h, c) = Create(t).SplitWakeWord(text);
        Assert.Equal(heard, h);
        Assert.Equal(command, c);
    }

    [Fact]
    public void Custom_wake_word()
    {
        using var t = new TempDir();
        var (h, c) = Create(t, "Jarvis").SplitWakeWord("Jarvis, lock the screen");
        Assert.True(h);
        Assert.Equal("lock the screen", c);
    }
}

public class IndexTests
{
    private static IndexDatabase CreateWithApps(TempDir t, params (string Name, string Kind)[] apps)
    {
        var db = new IndexDatabase(t.File("index.db"));
        using var c = db.Open();
        var id = 0;
        foreach (var (name, kind) in apps)
        {
            id++;
            var norm = TextNorm.Normalize(name);
            IndexDatabase.Exec(c, "INSERT INTO applications(id,name,name_norm,kind,launch_kind,launch,exe_path,source,updated) VALUES($i,$n,$nn,$k,'file',$l,$l,'test',1)",
                ("$i", id), ("$n", name), ("$nn", norm), ("$k", kind), ("$l", $@"C:\fake\{name}.exe"));
            IndexDatabase.Exec(c, "INSERT INTO applications_fts(rowid,name_norm,publisher_norm) VALUES($i,$nn,'')", ("$i", id), ("$nn", norm));
        }
        return db;
    }

    [Fact]
    public void Finds_apps_with_typos_prefixes_and_turkish_suffixes()
    {
        using var t = new TempDir();
        var search = new SearchService(CreateWithApps(t, ("Google Chrome", "browser"), ("Hesap Makinesi", "app"), ("VALORANT", "game"), ("Discord", "app")));
        Assert.Equal("Google Chrome", search.SearchApplications("chrome").First().Name);
        Assert.Equal("VALORANT", search.SearchApplications("valorant").First().Name);
        Assert.Equal("Hesap Makinesi", search.SearchApplications("hesap makinesini").First().Name);
        Assert.Equal("Discord", search.SearchApplications("discrod").First().Name);
        Assert.Empty(search.SearchApplications("zzzzqqqq"));
    }

    [Fact]
    public void Intent_router_handles_simple_commands_and_defers_the_rest()
    {
        using var t = new TempDir();
        var router = new IntentRouter(new SearchService(CreateWithApps(t, ("Google Chrome", "browser"), ("Discord", "app"), ("Spotify", "app"), ("Notepad", "app"), ("Notepad++", "app"))));

        var tr = router.TryResolve("Astra, Discord'u kapat ve Spotify'ı aç");
        Assert.NotNull(tr);
        Assert.Equal(new[] { LocalIntentKind.Close, LocalIntentKind.Open }, tr!.Select(s => s.Kind).ToArray());
        Assert.Equal(new[] { "Discord", "Spotify" }, tr.Select(s => s.App.Name).ToArray());

        var en = router.TryResolve("open chrome");
        Assert.Equal("Google Chrome", en!.Single().App.Name);

        // Anything that needs reasoning must go to the LLM.
        Assert.Null(router.TryResolve("Chrome'u aç ve YouTube'da Misery'yi ara"));
        Assert.Null(router.TryResolve("click the red button"));
        Assert.Null(router.TryResolve("open the file report.pdf in downloads"));
    }

    [Fact]
    public void Watcher_applies_create_rename_delete()
    {
        using var t = new TempDir(outsideAppData: true);
        var db = new IndexDatabase(t.File("index.db"));
        var watcher = new IndexWatcher(db, new ComputerIndexer(db));
        var root = Path.Combine(t.Path, "data");
        Directory.CreateDirectory(root);
        using (var c = db.Open())
        {
            IndexDatabase.Exec(c, "INSERT INTO folders(id,path,name,name_norm,parent_id,modified) VALUES(1,$p,'t','t',0,0)", ("$p", t.Path));
            IndexDatabase.Exec(c, "INSERT INTO folders(id,path,name,name_norm,parent_id,modified) VALUES(2,$p,'data','data',1,0)", ("$p", root));
        }
        var file = Path.Combine(root, "Rapor Final.pdf");
        File.WriteAllText(file, "x");
        var search = new SearchService(db);

        watcher.Apply(new() { (WatcherChangeTypes.Created, file, null) });
        Assert.Single(search.SearchFiles("rapor final", root, "pdf"));

        var renamed = Path.Combine(root, "Sözleşme.pdf");
        File.Move(file, renamed);
        watcher.Apply(new() { (WatcherChangeTypes.Renamed, renamed, file) });
        Assert.Empty(search.SearchFiles("rapor final", root));
        Assert.Single(search.SearchFiles("sozlesme", root));            // Turkish-insensitive search

        File.Delete(renamed);
        watcher.Apply(new() { (WatcherChangeTypes.Deleted, renamed, null) });
        Assert.Empty(search.SearchFiles("sozlesme", root));
    }

    [Theory]
    [InlineData("last_week")]
    [InlineData("yesterday")]
    [InlineData("2026-01-01..2026-02-01")]
    public void Parses_date_ranges(string range) => Assert.NotNull(SearchService.ParseDateRange(range).From);

    [Fact]
    public void Resolves_known_locations()
    {
        Assert.EndsWith("Downloads", SearchService.ResolveLocation("Downloads"));
        Assert.EndsWith("Downloads", SearchService.ResolveLocation("indirilenler"));
        Assert.Equal(@"D:\x", SearchService.ResolveLocation(@"D:\x"));
    }
}

public class FileOpsTests
{
    [Fact]
    public void Protects_system_locations()
    {
        Assert.True(FileOps.IsProtected(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.True(FileOps.IsProtected(@"C:\"));
        Assert.False(FileOps.IsProtected(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "anything")));
    }

    [Fact]
    public void Create_move_copy_delete_are_verified()
    {
        using var t = new TempDir();
        var f = t.File("a.txt");
        Assert.True(FileOps.Create(f, "hello", false).Ok);
        Assert.False(FileOps.Create(f, "again", false).Ok);                 // no silent overwrite
        var sub = t.File("sub");
        Directory.CreateDirectory(sub);
        Assert.True(FileOps.Move(f, sub).Ok);                               // moving into a folder keeps the name
        Assert.True(File.Exists(System.IO.Path.Combine(sub, "a.txt")));
        Assert.True(FileOps.Copy(System.IO.Path.Combine(sub, "a.txt"), t.File("b.txt")).Ok);
        Assert.True(FileOps.Delete(t.File("b.txt")).Ok);
        Assert.False(File.Exists(t.File("b.txt")));
    }
}

public class PermissionTests
{
    private sealed class Confirmer(bool answer) : IConfirmationService
    {
        public int Asked;
        public Task<bool> ConfirmAsync(ConfirmRequest r, CancellationToken ct) { Asked++; return Task.FromResult(answer); }
    }

    private static (AstraRuntime Rt, Confirmer C, TempDir T) Create(bool answer = true)
    {
        var t = new TempDir();
        var settings = new SettingsStore(t.File("s.json"));
        var rt = new AstraRuntime(settings, t.File("index.db"), t.File("memory.db"));
        var c = new Confirmer(answer);
        rt.Tools.Confirmation = c;
        return (rt, c, t);
    }

    private static JsonObject Obj(params (string, string)[] kv)
    {
        var o = new JsonObject();
        foreach (var (k, v) in kv) o[k] = v;
        return o;
    }

    [Fact]
    public async Task Ask_level_confirms_once_per_task_then_remembers()
    {
        var (rt, c, t) = Create();
        using (t) using (rt)
        {
            rt.Settings.Current.Security.Permissions["files"] = PermissionLevel.Ask;
            var task = new TaskState();
            var a = await rt.Tools.ExecuteAsync("create_file", Obj(("path", t.File("1.txt")), ("content", "x")), task, default);
            var b = await rt.Tools.ExecuteAsync("create_file", Obj(("path", t.File("2.txt")), ("content", "x")), task, default);
            Assert.True(a.Ok && b.Ok);
            Assert.Equal(1, c.Asked);
            await rt.Tools.ExecuteAsync("create_file", Obj(("path", t.File("3.txt")), ("content", "x")), new TaskState(), default);
            Assert.Equal(2, c.Asked);      // a new task asks again
        }
    }

    [Fact]
    public async Task Deny_blocks_without_asking()
    {
        var (rt, c, t) = Create();
        using (t) using (rt)
        {
            rt.Settings.Current.Security.Permissions["files"] = PermissionLevel.Deny;
            var r = await rt.Tools.ExecuteAsync("create_file", Obj(("path", t.File("x.txt")), ("content", "x")), new TaskState(), default);
            Assert.False(r.Ok);
            Assert.Equal(0, c.Asked);
            Assert.False(File.Exists(t.File("x.txt")));
        }
    }

    [Fact]
    public async Task Declined_action_does_not_run_and_stops_the_task()
    {
        var (rt, c, t) = Create(answer: false);
        using (t) using (rt)
        {
            var victim = t.File("keep.txt");
            File.WriteAllText(victim, "x");
            var task = new TaskState();
            var r = await rt.Tools.ExecuteAsync("delete_file", Obj(("path", victim)), task, default);
            Assert.False(r.Ok);
            Assert.True(task.Declined);
            Assert.True(File.Exists(victim));
        }
    }

    [Fact]
    public async Task Destructive_actions_always_ask_even_when_allowed()
    {
        var (rt, c, t) = Create();
        using (t) using (rt)
        {
            rt.Settings.Current.Security.Permissions["files"] = PermissionLevel.Allow;
            var f = t.File("del.txt");
            File.WriteAllText(f, "x");
            var r = await rt.Tools.ExecuteAsync("delete_file", Obj(("path", f)), new TaskState(), default);
            Assert.Equal(1, c.Asked);
            Assert.True(r.Ok);
        }
    }

    [Fact]
    public async Task Protected_paths_are_rejected_before_any_prompt()
    {
        var (rt, c, t) = Create();
        using (t) using (rt)
        {
            var r = await rt.Tools.ExecuteAsync("delete_file", Obj(("path", @"C:\Windows\System32")), new TaskState(), default);
            Assert.False(r.Ok);
            Assert.Equal(0, c.Asked);
        }
    }

    [Fact]
    public async Task Validation_errors_never_reach_execution()
    {
        var (rt, c, t) = Create();
        using (t) using (rt)
        {
            var r = await rt.Tools.ExecuteAsync("press_key", Obj(("keys", "ctrl+banana")), new TaskState(), default);
            Assert.False(r.Ok);
            Assert.Equal(0, c.Asked);
            Assert.False((await rt.Tools.ExecuteAsync("no_such_tool", new JsonObject(), new TaskState(), default)).Ok);
        }
    }

    [Fact]
    public void Every_tool_has_a_valid_schema_and_unique_name()
    {
        var (rt, _, t) = Create();
        using (t) using (rt)
        {
            var names = rt.Tools.All.Select(x => x.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.True(names.Count >= 35);
            foreach (var tool in rt.Tools.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.Description));
                Assert.Equal("object", (string?)tool.Schema["type"]);
            }
        }
    }
}

public class SpokenNameTests
{
    [Theory]
    [InlineData("vs code")]
    [InlineData("vc kot")]
    [InlineData("vscode")]
    [InlineData("vs kodu")]
    public void Visual_studio_code_is_found_from_spoken_forms(string heard)
    {
        using var t = new TempDir();
        var db = new IndexDatabase(t.File("i.db"));
        using (var c = db.Open())
        {
            var i = 0;
            foreach (var n in new[] { "Visual Studio Code", "Discord", "Valorant", "Google Chrome" })
            {
                i++;
                var norm = TextNorm.Normalize(n);
                IndexDatabase.Exec(c, "INSERT INTO applications(id,name,name_norm,kind,launch_kind,launch,exe_path,source,updated) VALUES($i,$n,$nn,'app','file',$l,$l,'t',1)", ("$i", i), ("$n", n), ("$nn", norm), ("$l", $@"C:\x\{n}.exe"));
                IndexDatabase.Exec(c, "INSERT INTO applications_fts(rowid,name_norm,publisher_norm) VALUES($i,$nn,'')", ("$i", i), ("$nn", norm));
            }
        }
        Assert.Equal("Visual Studio Code", new SearchService(db).SearchApplications(heard).First().Name);
        // and the local router accepts it without an LLM
        Assert.NotNull(new IntentRouter(new SearchService(db)).TryResolve(heard + "'u aç"));
    }
}
