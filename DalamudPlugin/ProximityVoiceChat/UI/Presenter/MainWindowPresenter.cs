using Dalamud.Plugin.Services;
using Reactive.Bindings;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.UI.View;
using ProximityVoiceChat.Extensions;
using ProximityVoiceChat.Input;
using AsyncAwaitBestPractices;

namespace ProximityVoiceChat.UI.Presenter;

public class MainWindowPresenter(
    MainWindow view,
    Configuration configuration,
    IClientState clientState,
    PushToTalkController pushToTalkController,
    VoiceRoomManager voiceRoomManager,
    ILogger logger) : IPluginUIPresenter, IDisposable
{
    public IPluginUIView View => this.view;

    private readonly MainWindow view = view ?? throw new ArgumentNullException(nameof(view));
    private readonly Configuration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly IClientState clientState = clientState ?? throw new ArgumentNullException(nameof(clientState));
    private readonly PushToTalkController pushToTalkController = pushToTalkController ?? throw new ArgumentNullException(nameof(pushToTalkController));
    private readonly IAudioDeviceController audioDeviceController = pushToTalkController;
    private readonly VoiceRoomManager voiceRoomManager = voiceRoomManager ?? throw new ArgumentNullException(nameof(voiceRoomManager));
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
        Bind(this.view.PublicRoom,
            b => { this.configuration.PublicRoom = b; this.configuration.Save(); }, this.configuration.PublicRoom);
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
