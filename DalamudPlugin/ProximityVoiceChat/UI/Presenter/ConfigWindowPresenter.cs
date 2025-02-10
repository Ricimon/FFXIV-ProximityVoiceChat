using System;
using Dalamud.Plugin.Services;
using ProximityVoiceChat.Input;
using ProximityVoiceChat.UI.View;
using Reactive.Bindings;
using WindowsInput.Events;

namespace ProximityVoiceChat.UI.Presenter;

public class ConfigWindowPresenter(
    ConfigWindow view,
    Configuration configuration,
    IFramework framework,
    PushToTalkController pushToTalkController,
    VoiceRoomManager voiceRoomManager,
    InputEventSource inputEventSource) : IPluginUIPresenter, IDisposable
{
    public IPluginUIView View => this.view;

    private readonly ConfigWindow view = view ?? throw new ArgumentNullException(nameof(view));
    private readonly Configuration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly IFramework framework = framework ?? throw new ArgumentNullException(nameof(framework));
    private readonly PushToTalkController pushToTalkController = pushToTalkController ?? throw new ArgumentNullException(nameof(pushToTalkController));
    private readonly IAudioDeviceController audioDeviceController = pushToTalkController;
    private readonly VoiceRoomManager voiceRoomManager = voiceRoomManager ?? throw new ArgumentNullException(nameof(voiceRoomManager));
    private readonly InputEventSource inputEventSource = inputEventSource ?? throw new ArgumentNullException(nameof(inputEventSource));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public void SetupBindings()
    {
        BindVariables();
        BindActions();
    }

    private void BindVariables()
    {
        Bind(this.view.SelectedAudioInputDeviceIndex,
            b => this.audioDeviceController.AudioRecordingDeviceIndex = b, this.audioDeviceController.AudioRecordingDeviceIndex);
        Bind(this.view.SelectedAudioOutputDeviceIndex,
            b => this.audioDeviceController.AudioPlaybackDeviceIndex = b, this.audioDeviceController.AudioPlaybackDeviceIndex);
        Bind(this.view.PlayingBackMicAudio,
            b => 
            {
                this.audioDeviceController.PlayingBackMicAudio = b;
                this.voiceRoomManager.PushPlayerAudioState();
            },
            this.audioDeviceController.PlayingBackMicAudio);
        Bind(this.view.PushToTalk,
            b => 
            {
                this.configuration.PushToTalk = b;
                this.configuration.Save();
                this.pushToTalkController.UpdateListeners();
            },
            this.configuration.PushToTalk);
        Bind(this.view.SuppressNoise,
            b => { this.configuration.SuppressNoise = b; this.configuration.Save(); }, this.configuration.SuppressNoise);

        Bind(this.view.MasterVolume,
            f => { this.configuration.MasterVolume = f; this.configuration.Save(); }, this.configuration.MasterVolume);
        Bind(this.view.AudioFalloffType,
            t => { this.configuration.FalloffModel.Type = t; this.configuration.Save(); }, this.configuration.FalloffModel.Type);
        Bind(this.view.AudioFalloffMinimumDistance,
            f => { this.configuration.FalloffModel.MinimumDistance = f; this.configuration.Save(); }, this.configuration.FalloffModel.MinimumDistance);
        Bind(this.view.AudioFalloffMaximumDistance,
            f => { this.configuration.FalloffModel.MaximumDistance = f; this.configuration.Save(); }, this.configuration.FalloffModel.MaximumDistance);
        Bind(this.view.AudioFalloffFactor,
            f => { this.configuration.FalloffModel.FalloffFactor = f; this.configuration.Save(); }, this.configuration.FalloffModel.FalloffFactor);
        Bind(this.view.MuteDeadPlayers,
            b => { this.configuration.MuteDeadPlayers = b; this.configuration.Save(); }, this.configuration.MuteDeadPlayers);
        Bind(this.view.MuteDeadPlayersDelayMs,
            f => { this.configuration.MuteDeadPlayersDelayMs = f; this.configuration.Save(); }, this.configuration.MuteDeadPlayersDelayMs);
        Bind(this.view.MuteOutOfMapPlayers,
            b => { this.configuration.MuteOutOfMapPlayers = b; this.configuration.Save(); }, this.configuration.MuteOutOfMapPlayers);

        Bind(this.view.PrintLogsToChat,
            b => { this.configuration.PrintLogsToChat = b; this.configuration.Save(); }, this.configuration.PrintLogsToChat);
        Bind(this.view.MinimumVisibleLogLevel,
            i => { this.configuration.MinimumVisibleLogLevel = i; this.configuration.Save(); }, this.configuration.MinimumVisibleLogLevel);
    }

    private void BindActions()
    {
        this.view.EditingPushToTalkKeybind.Subscribe(b => 
        {
            if (b)
            {
                this.inputEventSource.SubscribeToKeyDown(OnInputKeyDown);
            }
            else
            {
                this.inputEventSource.UnsubscribeToKeyDown(OnInputKeyDown);
            }
        });
        this.view.ClearPushToTalkKeybind.Subscribe(_ => 
        {
            this.configuration.PushToTalkKeybind = default;
            this.configuration.Save();
        });
    }

    private void Bind<T>(
        IReactiveProperty<T> reactiveProperty,
        Action<T> dataUpdateAction,
        T initialValue)
    {
        if (initialValue != null)
        {
            reactiveProperty.Value = initialValue;
        }
        reactiveProperty.Subscribe(dataUpdateAction);
    }

    private void OnInputKeyDown(KeyDown k)
    {
        this.configuration.PushToTalkKeybind = k.Key;
        this.configuration.Save();
        // This callback can be called from a non-framework thread, and UI values should only be modified
        // on the framework thread (or else the game can crash)
        this.framework.Run(() =>
        {
            if (this.view.EditingPushToTalkKeybind.Value)
            {
                this.view.EditingPushToTalkKeybind.Value = false;
            }
        });
    }
}
