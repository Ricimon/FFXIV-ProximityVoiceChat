using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Ninject;
using Ninject.Extensions.Factory;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.Ninject;

namespace ProximityVoiceChat;

public sealed class PluginInitializer : IDalamudPlugin
{
    public static string Name => "ProximityVoiceChat";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get ; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IAddonEventManager AddonEventManager { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly IKernel kernel;

    public PluginInitializer()
    {
        this.kernel = new StandardKernel(new PluginModule(), new FuncModule());

        // Logging
        SIPSorcery.LogFactory.Set(this.kernel.Get<DalamudLoggerFactory>());

        // Entrypoint
        this.kernel.Get<Plugin>().Initialize();
    }

    public void Dispose()
    {
        this.kernel.Dispose();
    }
}
