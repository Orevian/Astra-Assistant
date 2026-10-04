using Astra.Core.Agent;
using Astra.Core.Browser;
using Astra.Core.Control;
using Astra.Core.Index;
using Astra.Core.Llm;
using Astra.Core.Memory;
using Astra.Core.Security;
using Astra.Core.Settings;
using Astra.Core.Tools;

namespace Astra.Core.Assistant;

/// <summary>Composition root: every service the agent and tools need, created once.</summary>
public sealed class AstraRuntime : IDisposable
{
    public SettingsStore Settings { get; }
    public IndexService Index { get; }
    public WindowManager Windows { get; }
    public AppController Apps { get; }
    public InputController Input { get; }
    public UiAutomationService Ui { get; }
    public BrowserController Browser { get; }
    public MemoryStore Memory { get; }
    public VisionService Vision { get; }
    public ToolExecutor Tools { get; }
    public AgentEngine Agent { get; }

    public AstraRuntime(SettingsStore settings, string? indexPath = null, string? memoryPath = null)
    {
        Settings = settings;
        Index = new IndexService(settings, indexPath);
        Windows = new WindowManager();
        Apps = new AppController(Windows);
        Input = new InputController();
        Ui = new UiAutomationService();
        Browser = new BrowserController(Index.Search, settings, Windows);
        Memory = new MemoryStore(settings, memoryPath);
        Vision = new VisionService(this);
        Tools = new ToolExecutor(this, BuiltInTools.Create());
        Agent = new AgentEngine(this);
    }

    /// <summary>Builds the chat client for the configured provider, or explains what is missing.</summary>
    public (IChatClient? Client, string? Error) CreateChatClient()
    {
        var ai = Settings.Current.Ai;
        return ChatClientFactory.Create(ai.ProviderId, ai.Model, ai.CustomEndpoint, CredentialStore.Load);
    }

    public void Dispose()
    {
        Index.Dispose();
        Browser.Dispose();
        Ui.Dispose();
    }
}
