using System;
using ProximityVoiceChat.UI.View;

namespace ProximityVoiceChat.UI.Presenter;

public class ConfigWindowPresenter : IPluginUIPresenter
{
    public IPluginUIView View => this.view;

    private readonly ConfigWindow view;

    // There's supposed to be an IConfigWindow, but it's a debug config window so whatever
    public ConfigWindowPresenter(ConfigWindow view)
    {
        this.view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public void SetupBindings()
    {

    }
}
