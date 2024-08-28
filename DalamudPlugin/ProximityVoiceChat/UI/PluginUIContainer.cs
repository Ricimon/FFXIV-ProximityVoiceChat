﻿using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using System;
using ProximityVoiceChat.UI.Presenter;

namespace ProximityVoiceChat.UI;

// It is good to have this be disposable in general, in case you ever need it
// to do any cleanup
public class PluginUIContainer : IDalamudHook
{
    private readonly IPluginUIPresenter[] pluginUIPresenters;
    private readonly MainWindowPresenter mainWindowPresenter;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly WindowSystem windowSystem;

    public PluginUIContainer(
        IPluginUIPresenter[] pluginUIPresenters,
        MainWindowPresenter mainWindowPresenter,
        IDalamudPluginInterface pluginInterface,
        WindowSystem windowSystem)
    {
        this.pluginUIPresenters = pluginUIPresenters ?? throw new ArgumentNullException(nameof(pluginUIPresenters));
        this.mainWindowPresenter = mainWindowPresenter ?? throw new ArgumentNullException(nameof(mainWindowPresenter));
        this.pluginInterface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface));
        this.windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));

        foreach (var pluginUIPresenter in this.pluginUIPresenters)
        {
            pluginUIPresenter.SetupBindings();
        }
    }

    public void Dispose()
    {
        this.pluginInterface.UiBuilder.Draw -= Draw;
        this.pluginInterface.UiBuilder.OpenMainUi -= ShowMainWindow;
    }

    public void HookToDalamud()
    {
        this.pluginInterface.UiBuilder.Draw += Draw;
        this.pluginInterface.UiBuilder.OpenMainUi += ShowMainWindow;
    }

    public void Draw()
    {
        // This is our only draw handler attached to UIBuilder, so it needs to be
        // able to draw any windows we might have open.
        // Each method checks its own visibility/state to ensure it only draws when
        // it actually makes sense.
        // There are other ways to do this, but it is generally best to keep the number of
        // draw delegates as low as possible.

        foreach (var pluginUIPresenter in this.pluginUIPresenters)
        {
            pluginUIPresenter.View.Draw();
        }
    }

    private void ShowMainWindow()
    {
        this.mainWindowPresenter.View.Visible = true;
    }
}
