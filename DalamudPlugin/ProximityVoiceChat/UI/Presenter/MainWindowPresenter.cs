using System;
using System.Reactive.Linq;
using AsyncAwaitBestPractices;
using Dalamud.Plugin.Services;
using ProximityVoiceChat.Extensions;
using ProximityVoiceChat.Input;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.UI.View;
using Reactive.Bindings;

namespace ProximityVoiceChat.UI.Presenter;

public class MainWindowPresenter(
    MainWindow view,
    ConfigWindowPresenter configWindowPresenter,
    Configuration configuration,
    IClientState clientState,
    IAudioDeviceController audioDeviceController,
    VoiceRoomManager voiceRoomManager,
    ILogger logger) : IPluginUIPresenter
{
    public IPluginUIView View => this.view;

    private readonly MainWindow view = view;
    private readonly ConfigWindowPresenter configWindowPresenter = configWindowPresenter;
    private readonly Configuration configuration = configuration;
    private readonly IClientState clientState = clientState;
    private readonly IAudioDeviceController audioDeviceController = audioDeviceController;
    private readonly VoiceRoomManager voiceRoomManager = voiceRoomManager;
    private readonly ILogger logger = logger;

    public void SetupBindings()
    {
        BindVariables();
        BindActions();
        this.configWindowPresenter.SetupBindings();
    }

    private void BindVariables()
    {
        Bind(this.view.PublicRoom,
            b =>
            {
                this.configuration.PublicRoom = b; this.configuration.Save();
                this.voiceRoomManager.SignalingChannel?.ClearLatestError();
            },
            this.configuration.PublicRoom);
        Bind(this.view.RoomName,
            s => { this.configuration.RoomName = s; this.configuration.Save(); }, this.configuration.RoomName);
        Bind(this.view.RoomPassword,
            s => { this.configuration.RoomPassword = s; this.configuration.Save(); }, this.configuration.RoomPassword);
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
            this.voiceRoomManager.SignalingChannel?.ClearLatestError();
            if (this.view.PublicRoom.Value)
            {
                this.voiceRoomManager.JoinPublicVoiceRoom();
            }
            else
            {
                if (string.IsNullOrEmpty(this.view.RoomName.Value))
                {
                    var playerName = this.clientState.GetLocalPlayerFullName();
                    if (playerName == null)
                    {
                        this.logger.Error("Player name is null, cannot autofill private room name.");
                        return;
                    }
                    this.view.RoomName.Value = playerName;
                }
                this.voiceRoomManager.JoinPrivateVoiceRoom(this.view.RoomName.Value, this.view.RoomPassword.Value);
            }
        });

        this.view.LeaveVoiceRoom.Subscribe(_ => this.voiceRoomManager.LeaveVoiceRoom(false).SafeFireAndForget(ex => this.logger.Error(ex.ToString())));

        this.view.SetPeerVolume.Subscribe(pv =>
        {
            if (pv.volume == 1.0f)
            {
                this.configuration.PeerVolumes.Remove(pv.playerName);
            }
            else
            {
                this.configuration.PeerVolumes[pv.playerName] = pv.volume;
            }
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
}
