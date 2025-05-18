using Dalamud.Plugin.Services;
using ProximityVoiceChat.Input;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.UI.View;
using Reactive.Bindings;
using System;
using WindowsInput.Events;

namespace ProximityVoiceChat.UI.Presenter;

public class ConfigWindowPresenter(
    ConfigWindow view,
    Configuration configuration,
    IFramework framework,
    PushToTalkController pushToTalkController,
    VoiceRoomManager voiceRoomManager,
    InputEventSource inputEventSource,
    InputManager inputManager,
    ILogger logger)
{
    private readonly ConfigWindow view = view;
    private readonly Configuration configuration = configuration;
    private readonly IFramework framework = framework;
    private readonly PushToTalkController pushToTalkController = pushToTalkController;
    private readonly IAudioDeviceController audioDeviceController = pushToTalkController;
    private readonly VoiceRoomManager voiceRoomManager = voiceRoomManager;
    private readonly InputEventSource inputEventSource = inputEventSource;
    private readonly InputManager inputManager = inputManager;
    private readonly ILogger logger = logger;

    private bool keyDownListenerSubscribed;

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
        Bind(this.view.EnableSpatialization,
            b => { this.configuration.EnableSpatialization = b; this.configuration.Save(); }, this.configuration.EnableSpatialization);
        Bind(this.view.MuteDeadPlayers,
            b => { this.configuration.MuteDeadPlayers = b; this.configuration.Save(); }, this.configuration.MuteDeadPlayers);
        Bind(this.view.MuteDeadPlayersDelayMs,
            f => { this.configuration.MuteDeadPlayersDelayMs = f; this.configuration.Save(); }, this.configuration.MuteDeadPlayersDelayMs);
        Bind(this.view.MuteOutOfMapPlayers,
            b => { this.configuration.MuteOutOfMapPlayers = b; this.configuration.Save(); }, this.configuration.MuteOutOfMapPlayers);

        Bind(this.view.PlayRoomJoinAndLeaveSounds,
            b => { this.configuration.PlayRoomJoinAndLeaveSounds = b; this.configuration.Save(); }, this.configuration.PlayRoomJoinAndLeaveSounds);
        Bind(this.view.KeybindsRequireGameFocus,
            b => { this.configuration.KeybindsRequireGameFocus = b; this.configuration.Save(); }, this.configuration.KeybindsRequireGameFocus);
        Bind(this.view.PrintLogsToChat,
            b => { this.configuration.PrintLogsToChat = b; this.configuration.Save(); }, this.configuration.PrintLogsToChat);
        Bind(this.view.MinimumVisibleLogLevel,
            i => { this.configuration.MinimumVisibleLogLevel = i; this.configuration.Save(); }, this.configuration.MinimumVisibleLogLevel);
    }

    private void BindActions()
    {
        this.view.KeybindBeingEdited.Subscribe(k => 
        {
            if (k != Keybind.None && !this.keyDownListenerSubscribed)
            {
                this.inputEventSource.SubscribeToKeyDown(OnInputKeyDown);
                this.keyDownListenerSubscribed = true;
            }
            else if (k == Keybind.None && this.keyDownListenerSubscribed)
            {
                this.inputEventSource.UnsubscribeToKeyDown(OnInputKeyDown);
                this.keyDownListenerSubscribed = false;
            }
        });
        this.view.ClearKeybind.Subscribe(k => 
        {
            switch(k)
            {
                case Keybind.PushToTalk:
                    this.configuration.PushToTalkKeybind = default;
                    break;
                case Keybind.MuteMic:
                    this.configuration.MuteMicKeybind = default;
                    break;
                case Keybind.Deafen:
                    this.configuration.DeafenKeybind = default;
                    break;
                default:
                    return;
            }
            this.configuration.Save();
            this.inputManager.UpdateListeners();
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
        // This callback can be called from a non-framework thread, and UI values should only be modified
        // on the framework thread (or else the game can crash)
        this.framework.Run(() =>
        {
            var editedKeybind = this.view.KeybindBeingEdited.Value;
            this.view.KeybindBeingEdited.Value = Keybind.None;

            switch (editedKeybind)
            {
                case Keybind.PushToTalk:
                    this.configuration.PushToTalkKeybind = k.Key;
                    break;
                case Keybind.MuteMic:
                    this.configuration.MuteMicKeybind = k.Key;
                    break;
                case Keybind.Deafen:
                    this.configuration.DeafenKeybind = k.Key;
                    break;
                default:
                    return;
            }
            this.configuration.Save();
            this.inputManager.UpdateListeners();
        });
    }
}
