﻿using Dalamud.Interface.Windowing;
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
        Bind<IDalamudPluginInterface>().ToConstant(PluginInitializer.PluginInterface).InTransientScope();
        Bind<ICommandManager>().ToConstant(PluginInitializer.CommandManager).InTransientScope();
        Bind<IChatGui>().ToConstant(PluginInitializer.ChatGui).InTransientScope();
        Bind<IClientState>().ToConstant(PluginInitializer.ClientState).InTransientScope();
        Bind<ICondition>().ToConstant(PluginInitializer.Condition).InTransientScope();
        Bind<IDutyState>().ToConstant(PluginInitializer.DutyState).InTransientScope();
        Bind<IDataManager>().ToConstant(PluginInitializer.DataManager).InTransientScope();
        Bind<IObjectTable>().ToConstant(PluginInitializer.ObjectTable).InTransientScope();
        Bind<IGameGui>().ToConstant(PluginInitializer.GameGui).InTransientScope();
        Bind<IAddonEventManager>().ToConstant(PluginInitializer.AddonEventManager).InTransientScope();
        Bind<IAddonLifecycle>().ToConstant(PluginInitializer.AddonLifecycle).InTransientScope();
        Bind<IFramework>().ToConstant(PluginInitializer.Framework).InTransientScope();
        Bind<ITextureProvider>().ToConstant(PluginInitializer.TextureProvider).InTransientScope();
        Bind<IPluginLog>().ToConstant(PluginInitializer.Log).InTransientScope();

        // Plugin classes
        Bind<Plugin>().ToSelf().InSingletonScope();
        Bind<IDalamudHook>().To<PluginUIContainer>().InSingletonScope();
        Bind<IDalamudHook>().To<CommandDispatcher>().InSingletonScope();
        Bind<Configuration>().ToMethod(GetConfiguration).InSingletonScope();
        Bind<InputEventSource>().ToSelf().InSingletonScope();
        Bind<IAudioDeviceController, PushToTalkController>().To<PushToTalkController>().InSingletonScope();
        Bind<IAudioDeviceController>().To<AudioDeviceController>().WhenInjectedInto<PushToTalkController>().InSingletonScope();
        Bind<VoiceRoomManager>().ToSelf().InSingletonScope();
        Bind<MapManager>().ToSelf().InSingletonScope();
        Bind<WebRTCDataChannelHandler>().ToSelf();
        Bind<WebRTCDataChannelHandler.IFactory>().ToFactory();

        // Views and Presenters
        Bind<WindowSystem>().ToMethod(_ => new(PluginInitializer.Name)).InSingletonScope();
        Bind<IPluginUIView, MainWindow>().To<MainWindow>().InSingletonScope();
        Bind<IPluginUIPresenter, MainWindowPresenter>().To<MainWindowPresenter>().InSingletonScope();
        Bind<IPluginUIView, ConfigWindow>().To<ConfigWindow>().InSingletonScope();
        Bind<IPluginUIPresenter, ConfigWindowPresenter>().To<ConfigWindowPresenter>().InSingletonScope();

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
