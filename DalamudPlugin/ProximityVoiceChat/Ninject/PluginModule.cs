using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Ninject.Activation;
using Ninject.Extensions.Factory;
using Ninject.Modules;
using ProximityVoiceChat.Input;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.UI;
using ProximityVoiceChat.UI.Presenter;
using ProximityVoiceChat.UI.View;
using ProximityVoiceChat.WebRTC;

namespace ProximityVoiceChat.Ninject;

public class PluginModule : NinjectModule
{
    public override void Load()
    {
        // Dalamud services
        // TODO: Deprecate these
        Bind<IDalamudPluginInterface>().ToConstant(PluginInitializer.PluginInterface).InTransientScope();
        Bind<ICommandManager>().ToConstant(PluginInitializer.CommandManager).InTransientScope();
        Bind<IChatGui>().ToConstant(PluginInitializer.ChatGui).InTransientScope();
        Bind<IFramework>().ToConstant(PluginInitializer.Framework).InTransientScope();
        Bind<IPluginLog>().ToConstant(PluginInitializer.Log).InTransientScope();

        // Dalamud services
        Bind<DalamudServices>().ToSelf();

        // Plugin classes
        Bind<Plugin>().ToSelf().InSingletonScope();
        Bind<IDalamudHook>().To<PluginUIContainer>().InSingletonScope();
        Bind<IDalamudHook>().To<CommandDispatcher>().InSingletonScope();
        Bind<Configuration>().ToMethod(GetConfiguration).InSingletonScope();
        Bind<InputEventSource>().ToSelf().InSingletonScope();
        Bind<InputManager>().ToSelf().InSingletonScope();
        Bind<IAudioDeviceController, PushToTalkController>().To<PushToTalkController>().InSingletonScope();
        Bind<IAudioDeviceController>().To<AudioDeviceController>().WhenInjectedInto<PushToTalkController>().InSingletonScope();
        Bind<VoiceRoomManager>().ToSelf().InSingletonScope();
        Bind<Spatializer>().ToSelf().InSingletonScope();
        Bind<MapManager>().ToSelf().InSingletonScope();
        Bind<WebRTCDataChannelHandler>().ToSelf();
        Bind<WebRTCDataChannelHandler.IFactory>().ToFactory();

        // Views and Presenters
        Bind<WindowSystem>().ToMethod(_ => new(PluginInitializer.Name)).InSingletonScope();
        Bind<IPluginUIView, MainWindow>().To<MainWindow>().InSingletonScope();
        Bind<IPluginUIPresenter, MainWindowPresenter>().To<MainWindowPresenter>().InSingletonScope();
        Bind<ConfigWindow>().ToSelf().InSingletonScope();
        Bind<ConfigWindowPresenter>().ToSelf().InSingletonScope();

        Bind<ILogger>().To<DalamudLogger>();
        Bind<DalamudLoggerFactory>().ToSelf();
    }

    private Configuration GetConfiguration(IContext context)
    {
        var configuration = 
            PluginInitializer.PluginInterface.GetPluginConfig() as Configuration
            ?? new Configuration();
        configuration.Initialize(PluginInitializer.PluginInterface);
        return configuration;
    }
}
