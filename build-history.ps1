<#
  Splits the project into logical, real commits (one per component) on a NEW branch called "history".
  Every commit contains the actual files of that component and a message describing them.
  Nothing is pushed and your current branch is not touched.

  After running it, review with:   git log --oneline history
  To make it your main history:    git push --force-with-lease origin history:main   (this REWRITES main on GitHub)
#>
# "Continue", not "Stop": Windows PowerShell 5.1 turns git's harmless stderr output into terminating errors.
# Failures are detected through $LASTEXITCODE instead.
$ErrorActionPreference = "Continue"
# Works from the repo root or from tools\.
$root = if (Test-Path (Join-Path $PSScriptRoot "Astra.sln")) { $PSScriptRoot } else { Split-Path $PSScriptRoot -Parent }
Set-Location $root

$groups = @(
    @{ Msg = "chore: solution skeleton, .NET 10 + WinUI 3 projects, gitignore";
       Paths = @(".gitignore", "Astra.sln", "src/Astra.Core/Astra.Core.csproj", "src/Astra.App/Astra.App.csproj", "src/Astra.App/app.manifest", "src/Astra.App/icon.ico") },
    @{ Msg = "feat(core): settings model with debounced atomic JSON store";
       Paths = @("src/Astra.Core/Settings") },
    @{ Msg = "feat(core): AI provider catalog, connection tests and Credential Manager key storage";
       Paths = @("src/Astra.Core/Providers", "src/Astra.Core/Security") },
    @{ Msg = "feat(i18n): Turkish/English localization layer with reverse lookup";
       Paths = @("src/Astra.Core/Localization") },
    @{ Msg = "feat(index): SQLite + FTS5 schema and Turkish-aware text normalization";
       Paths = @("src/Astra.Core/Index/IndexDatabase.cs", "src/Astra.Core/Index/TextNorm.cs") },
    @{ Msg = "feat(index): application, game and browser-profile discovery plus parallel full scan";
       Paths = @("src/Astra.Core/Index/ApplicationScanner.cs", "src/Astra.Core/Index/ComputerIndexer.cs") },
    @{ Msg = "feat(index): scoped search, spoken-name aliases, incremental watcher and index service";
       Paths = @("src/Astra.Core/Index/SearchService.cs", "src/Astra.Core/Index/AppAliases.cs", "src/Astra.Core/Index/IndexWatcher.cs", "src/Astra.Core/Index/IndexService.cs") },
    @{ Msg = "feat(control): Win32 input, windows, display enumeration, app launch/close, UI Automation";
       Paths = @("src/Astra.Core/Native", "src/Astra.Core/Control", "src/Astra.Core/System") },
    @{ Msg = "feat(browser): URL and search handling, YouTube first-result lookup, DevTools session";
       Paths = @("src/Astra.Core/Browser") },
    @{ Msg = "feat(llm): tool-calling chat clients for OpenAI-compatible, Anthropic, Gemini and Ollama";
       Paths = @("src/Astra.Core/Llm") },
    @{ Msg = "feat(tools): tool framework with validate/execute/verify pipeline and permission gating";
       Paths = @("src/Astra.Core/Tools/ToolBase.cs", "src/Astra.Core/Tools/ToolExecutor.cs") },
    @{ Msg = "feat(tools): built-in tools for apps, browser, input, files, system and memory";
       Paths = @("src/Astra.Core/Tools") },
    @{ Msg = "feat(agent): local-first intent router, language detection and tool-calling agent loop";
       Paths = @("src/Astra.Core/Agent") },
    @{ Msg = "feat(memory): local memory store with sensitive-content filter; feat(vision): screen locate/ask";
       Paths = @("src/Astra.Core/Memory", "src/Astra.Core/Assistant/VisionService.cs", "src/Astra.Core/Assistant/AstraRuntime.cs") },
    @{ Msg = "feat(voice): microphone capture, VAD, local Whisper, wake word, push-to-talk hotkey, TTS with live spectrum";
       Paths = @("src/Astra.Core/Voice") },
    @{ Msg = "feat(assistant): AssistantCore state machine tying voice, agent, tools and speech together";
       Paths = @("src/Astra.Core/Assistant") },
    @{ Msg = "feat(ui): app shell, Fluent settings pages, home console and shared SettingsCard";
       Paths = @("src/Astra.App/App.xaml", "src/Astra.App/App.xaml.cs", "src/Astra.App/MainWindow.xaml", "src/Astra.App/MainWindow.xaml.cs",
                 "src/Astra.App/Controls", "src/Astra.App/Pages", "src/Astra.App/Localizer.cs") },
    @{ Msg = "feat(ui): transparent overlays (HUD, AI cursor, speech bubble, spectrum, notifications), tray and multi-monitor placement";
       Paths = @("src/Astra.App/Overlays", "src/Astra.App/TrayIcon.cs") },
    @{ Msg = "feat(ui): first-run wizard and background scan controller";
       Paths = @("src/Astra.App/Onboarding", "src/Astra.App/ScanController.cs") },
    @{ Msg = "test: unit tests for normalization, search, routing, permissions, wake word, memory and index watcher";
       Paths = @("tests") },
    @{ Msg = "build: self-contained publish script and Inno Setup installer";
       Paths = @("build.ps1", "installer") },
    @{ Msg = "docs: bilingual README and release notes";
       Paths = @("README.md", "release-readme.md", "tools") }
)

# Folder not a repo yet? Set it up first.
git rev-parse --is-inside-work-tree 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    git init -q
    git branch -M main
    git remote add origin https://github.com/Orevian/Astra-Assistant.git 2>$null
    Write-Host "Initialised a local git repository." -ForegroundColor Yellow
}
# A branch needs at least one commit to branch away from: snapshot the current state on main.
git rev-parse HEAD 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    git add -A
    git commit -q -m "Astra v1.0.1 (snapshot)"
}
$current = git branch --show-current
if (git branch --list history) { throw "Branch 'history' already exists. Delete it first: git branch -D history" }

git checkout --orphan history | Out-Null
git rm -r --cached . -q 2>$null | Out-Null

$made = 0
foreach ($g in $groups) {
    $existing = $g.Paths | Where-Object { Test-Path $_ }
    if (-not $existing) { continue }
    git add -- $existing 2>$null
    if (git diff --cached --quiet) { continue }          # nothing new in this group: never create empty commits
    git commit -q -m $g.Msg
    $made++
}

# Anything not covered above (so the final tree equals your real project).
git add -A
if (-not (git diff --cached --quiet)) { git commit -q -m "chore: remaining project files"; $made++ }

git checkout $current -q
Write-Host "Created $made real commits on branch 'history'. Your '$current' branch is unchanged." -ForegroundColor Green
Write-Host "Review:  git log --oneline history"
Write-Host "Publish: git push --force-with-lease origin history:main   (rewrites main on GitHub, so be sure first)"
