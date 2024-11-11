using Dalamud.Plugin.Services;
using Reactive.Bindings;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.UI.View;
using ProximityVoiceChat.Extensions;
using WindowsInput.Events;
using ProximityVoiceChat.Input;

namespace ProximityVoiceChat.UI.Presenter;

public class MainWindowPresenter(
    IMainWindow view,
    Configuration configuration,
    IClientState clientState,
    IFramework framework,
    PushToTalkController pushToTalkController,
    VoiceRoomManager voiceRoomManager,
    InputEventSource inputEventSource,
    ILogger logger) : IPluginUIPresenter, IDisposable
{
    public IPluginUIView View => this.view;

    private readonly IMainWindow view = view ?? throw new ArgumentNullException(nameof(view));
    private readonly Configuration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly IClientState clientState = clientState ?? throw new ArgumentNullException(nameof(clientState));
    private readonly IFramework framework = framework ?? throw new ArgumentNullException(nameof(framework));
    private readonly PushToTalkController pushToTalkController = pushToTalkController ?? throw new ArgumentNullException(nameof(pushToTalkController));
    private readonly IAudioDeviceController audioDeviceController = pushToTalkController;
    private readonly VoiceRoomManager voiceRoomManager = voiceRoomManager ?? throw new ArgumentNullException(nameof(voiceRoomManager));
    private readonly InputEventSource inputEventSource = inputEventSource ?? throw new ArgumentNullException(nameof(inputEventSource));
    private readonly ILogger logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly CompositeDisposable disposables = [];

    public void Dispose()
    {
        this.disposables.Dispose();
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

        Bind(this.view.PublicRoom,
            b => { this.configuration.PublicRoom = b; this.configuration.Save(); }, this.configuration.PublicRoom);
        Bind(this.view.RoomName,
            s => { this.configuration.RoomName = s; this.configuration.Save(); }, this.configuration.RoomName);
        Bind(this.view.RoomPassword,
            s => { this.configuration.RoomPassword = s; this.configuration.Save(); }, this.configuration.RoomPassword);

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
        Bind(this.view.MuteOutOfMapPlayers,
            b => { this.configuration.MuteOutOfMapPlayers = b; this.configuration.Save(); }, this.configuration.MuteOutOfMapPlayers);

        Bind(this.view.PrintLogsToChat,
            b => { this.configuration.PrintLogsToChat = b; this.configuration.Save(); }, this.configuration.PrintLogsToChat);
        Bind(this.view.MinimumVisibleLogLevel,
            i => { this.configuration.MinimumVisibleLogLevel = i; this.configuration.Save(); }, this.configuration.MinimumVisibleLogLevel);
    }

    private void BindActions()
    {
        this.view.MuteMic.Subscribe(b =>
        {
            this.audioDeviceController.MuteMic = b;
            this.voiceRoomManager.PushPlayerAudioState();
        });
        this.view.Deafen.Subscribe(b =>
        {
            this.audioDeviceController.Deafen = b;
            this.voiceRoomManager.PushPlayerAudioState();
        });

        this.view.JoinVoiceRoom.Subscribe(_ =>
        {
            var playerName = this.clientState.GetLocalPlayerFullName();
            if (playerName == null)
            {
                this.logger.Error("Player name is null, cannot join voice room.");
                return;
            }

            string roomName;
            if (this.view.PublicRoom.Value)
            {
                roomName = string.Empty;
            }
            else
            {
                if (string.IsNullOrEmpty(this.view.RoomName.Value))
                {
                    this.view.RoomName.Value = playerName;
                }
                roomName = this.view.RoomName.Value;
            }
            this.voiceRoomManager.JoinVoiceRoom(roomName, this.view.RoomPassword.Value);
        });

        this.view.LeaveVoiceRoom.Subscribe(_ => this.voiceRoomManager.LeaveVoiceRoom());

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
