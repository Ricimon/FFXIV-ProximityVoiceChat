using ProximityVoiceChat.UI.View;

namespace ProximityVoiceChat.UI.Presenter;

public interface IPluginUIPresenter
{
    IPluginUIView View { get; }

    void SetupBindings();
}
