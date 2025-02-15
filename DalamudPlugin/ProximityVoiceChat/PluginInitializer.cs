using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.MixedReality.WebRTC;
using Ninject;
using Ninject.Extensions.Factory;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.Ninject;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WebRtcVadSharp;

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
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get ; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IAddonEventManager AddonEventManager { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly StandardKernel kernel;

    public PluginInitializer()
    {
        // For whatever reason this is needed to load certain dlls
        NativeLibrary.SetDllImportResolver(typeof(PeerConnection).Assembly, (_, assembly, path) => NativeLibrary.Load("mrwebrtc.dll", assembly, path));
        NativeLibrary.SetDllImportResolver(typeof(WebRtcVad).Assembly, (_, assembly, path) => NativeLibrary.Load("WebRtcVad.dll", assembly, path));

        this.kernel = new StandardKernel(new PluginModule(), new FuncModule());

        // Logging
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        SIPSorcery.LogFactory.Set(this.kernel.Get<DalamudLoggerFactory>());

        // Entrypoint
        this.kernel.Get<Plugin>().Initialize();
    }

    public void Dispose()
    {
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        this.kernel.Dispose();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
#if DEBUG
        // There's a number of unobserved Task exceptions that can be thrown by 3rd party libraries
        // in this project, some of which seem to be unavoidable but harmless.
        this.kernel.Get<ILogger>().Error(e.Exception.ToString());
#else
        // So, lower the severity of the log in release builds.
        this.kernel.Get<ILogger>().Trace(e.Exception.ToString());
#endif
        e.SetObserved();
    }
}
