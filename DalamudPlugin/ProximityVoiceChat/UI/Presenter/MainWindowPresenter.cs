using Dalamud.Plugin.Services;
using Reactive.Bindings;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.UI.View;

namespace ProximityVoiceChat.UI.Presenter;

public class MainWindowPresenter(
    IMainWindow view,
    Configuration configuration,
    IObjectTable objectTable,
    AudioDeviceController audioInputController,
    VoiceRoomManager voiceRoomManager,
    ILogger logger) : IPluginUIPresenter, IDisposable
{
    public IPluginUIView View => this.view;

    private readonly IMainWindow view = view ?? throw new ArgumentNullException(nameof(view));
    private readonly Configuration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly IObjectTable objectTable = objectTable ?? throw new ArgumentNullException(nameof(objectTable));
    private readonly AudioDeviceController audioInputController = audioInputController ?? throw new ArgumentNullException(nameof(audioInputController));
    private readonly VoiceRoomManager voiceRoomManager = voiceRoomManager ?? throw new ArgumentNullException(nameof(voiceRoomManager));
    private readonly ILogger logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly CompositeDisposable disposables = new();

    public void Dispose()
    {
        this.disposables.Dispose();
    }

    public void SetupBindings()
    {
        BindVariables();
        BindActions();
    }

    private void BindVariables()
    {
        Bind(this.view.SelectedAudioInputDeviceIndex,
            b => this.audioInputController.AudioRecordingDeviceIndex = b, this.audioInputController.AudioRecordingDeviceIndex);
        Bind(this.view.SelectedAudioOutputDeviceIndex,
            b => this.audioInputController.AudioPlaybackDeviceIndex = b, this.audioInputController.AudioPlaybackDeviceIndex);
        Bind(this.view.PlayingBackMicAudio,
            b => this.audioInputController.PlayingBackMicAudio = b, this.audioInputController.PlayingBackMicAudio);

        Bind(this.view.PrintLogsToChat,
            b => { this.configuration.PrintLogsToChat = b; this.configuration.Save(); }, this.configuration.PrintLogsToChat);
        Bind(this.view.MinimumVisibleLogLevel,
            i => { this.configuration.MinimumVisibleLogLevel = i; this.configuration.Save(); }, this.configuration.MinimumVisibleLogLevel);
    }

    private void BindActions()
    {
        this.view.JoinVoiceRoom.Subscribe(_ => this.voiceRoomManager.JoinVoiceRoom());
        this.view.LeaveVoiceRoom.Subscribe(_ => this.voiceRoomManager.LeaveVoiceRoom());

        this.view.LogAllGameObjects.Subscribe(_ =>
        {
            foreach(var o in this.objectTable)
            {
                this.logger.Debug("Object address [{0}] name [{1}] gameObjectId [{2}] dataId [{3}] ownerId [{4}] objectIndex [{5}] objectKind [{6}] subKind [{7}] isValid [{8}]",
                    o.Address,
                    o.Name,
                    o.GameObjectId,
                    o.DataId,
                    o.OwnerId,
                    o.ObjectIndex,
                    o.ObjectKind,
                    o.SubKind,
                    o.IsValid());
            }
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
