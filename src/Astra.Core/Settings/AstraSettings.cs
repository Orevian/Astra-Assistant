namespace Astra.Core.Settings;

public enum ThemePreset { Minimal, Glass, Cyber, Neon, Classic, Terminal, Custom }
public enum VisualizerStyle { SpectrumBars, CircularSpectrum, Waveform, Pulse, Orb, Minimal, None }
public enum ScreenCorner { TopLeft, TopRight, BottomLeft, BottomRight, Center }
public enum ListenMode { WakeWord, PushToTalk }
public enum PermissionLevel { Ask, Allow, Deny }

public sealed class AiSettings
{
    public string ProviderId { get; set; } = "openai";
    public string Model { get; set; } = "";
    public string? CustomEndpoint { get; set; }
    public string VisionProviderId { get; set; } = "same";
    /// <summary>Model used for screen analysis when the vision provider differs from the chat provider. Empty picks a sensible default.</summary>
    public string VisionModel { get; set; } = "";
}

public sealed class VoiceSettings
{
    public string? MicrophoneId { get; set; }
    public ListenMode Mode { get; set; } = ListenMode.WakeWord;
    public string WakeWord { get; set; } = "Astra";
    public string PushToTalkKey { get; set; } = "Ctrl+Alt+Space";
    public string SttProvider { get; set; } = "whisper";
    /// <summary>Local Whisper model: tiny | base | small (small is far better at Turkish).</summary>
    public string WhisperModel { get; set; } = "small";
    /// <summary>"auto" follows the Windows display language, otherwise a BCP-47 tag such as "tr-TR".</summary>
    public string RecognitionLanguage { get; set; } = "auto";
    public bool Paused { get; set; }
    public string TtsProvider { get; set; } = "windows";
    public string? TtsVoice { get; set; }
    public double TtsSpeed { get; set; } = 1.0;
    public int TtsVolume { get; set; } = 80;
    public bool SpeakResponses { get; set; } = true;
    /// <summary>"auto" or a BCP-47 tag such as "tr-TR" / "en-US".</summary>
    public string ResponseLanguage { get; set; } = "auto";
}

public sealed class AppearanceSettings
{
    public ThemePreset Theme { get; set; } = ThemePreset.Glass;
    public string AppTheme { get; set; } = "system"; // system | light | dark
    public string IconId { get; set; } = "app"; // "app" = the bundled icon.ico
    public string? CustomIconPath { get; set; }
    public string AccentColor { get; set; } = "#7C5CFF";

    public bool HudEnabled { get; set; } = true;
    public bool AiCursorEnabled { get; set; } = true;
    public bool VisualizerEnabled { get; set; }
    public bool SpeechBubbleEnabled { get; set; }

    /// <summary>Device name of the monitor ("\.\DISPLAY2"); empty means the primary (1st) display.</summary>
    public string HudDisplay { get; set; } = "";
    public string NotificationDisplay { get; set; } = "";
    public ScreenCorner NotificationPosition { get; set; } = ScreenCorner.BottomRight;
    public ScreenCorner BubblePosition { get; set; } = ScreenCorner.BottomRight;
    public bool NotificationsEnabled { get; set; } = true;
    public bool AnimationsEnabled { get; set; } = true;

    public int HudOpacity { get; set; } = 92;
    public ScreenCorner HudPosition { get; set; } = ScreenCorner.BottomRight;
    public int CursorSize { get; set; } = 28;

    public VisualizerStyle Visualizer { get; set; } = VisualizerStyle.SpectrumBars;
    public int VisualizerSize { get; set; } = 100;
    public int VisualizerOpacity { get; set; } = 90;
    public ScreenCorner VisualizerPosition { get; set; } = ScreenCorner.TopRight;
}

public sealed class AutomationSettings
{
    public string? PreferredBrowserId { get; set; }
    public bool PreferUiAutomation { get; set; } = true;
    public bool AllowMouse { get; set; } = true;
    public bool AllowKeyboard { get; set; } = true;
    public bool VerifyActions { get; set; } = true;
    public int ActionDelayMs { get; set; } = 150;
    public int MaxAgentSteps { get; set; } = 20;
    public bool LocalFirst { get; set; } = true; // simple intents are resolved without an LLM
}

public sealed class SecuritySettings
{
    public Dictionary<string, PermissionLevel> Permissions { get; set; } = new()
    {
        ["applications"] = PermissionLevel.Allow,
        ["browser"] = PermissionLevel.Allow,
        ["files"] = PermissionLevel.Ask,
        ["input"] = PermissionLevel.Ask,
        ["terminal"] = PermissionLevel.Ask,
        ["system"] = PermissionLevel.Ask,
        ["power"] = PermissionLevel.Ask,
    };
    public int ConfirmDeleteOverFiles { get; set; } = 1;
    public bool AlwaysConfirmDestructive { get; set; } = true;
}

public sealed class IndexSettings
{
    public bool Completed { get; set; }
    public bool WatchChanges { get; set; } = true;
    public List<string> ExcludedFolders { get; set; } = new();
    public bool IndexFileMetadata { get; set; } = true;
}

public sealed class MemorySettings
{
    public bool Enabled { get; set; } = true;
    public int ShortTermTurns { get; set; } = 8;
}

public sealed class GeneralSettings
{
    public string AssistantName { get; set; } = "Astra";
    public string UiLanguage { get; set; } = "auto";
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool FirstRunCompleted { get; set; }
}

public sealed class AstraSettings
{
    public GeneralSettings General { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public VoiceSettings Voice { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public AutomationSettings Automation { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public IndexSettings Index { get; set; } = new();
    public MemorySettings Memory { get; set; } = new();
}
