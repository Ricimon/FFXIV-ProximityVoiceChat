using System;
using WindowsInput.Events;

namespace ProximityVoiceChat.Input;

public class InputManager
{
    private readonly Configuration configuration;
    private readonly InputEventSource inputEventSource;
    private readonly IAudioDeviceController audioDeviceController;
    private readonly VoiceRoomManager voiceRoomManager;

    private bool listenerSubscribed;

    public InputManager(
        Configuration configuration,
        InputEventSource inputEventSource,
        IAudioDeviceController audioDeviceController,
        VoiceRoomManager voiceRoomManager)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.inputEventSource = inputEventSource ?? throw new ArgumentNullException(nameof(inputEventSource));
        this.audioDeviceController = audioDeviceController ?? throw new ArgumentNullException(nameof(audioDeviceController));
        this.voiceRoomManager = voiceRoomManager ?? throw new ArgumentNullException(nameof(voiceRoomManager));

        UpdateListeners();
    }

    public void UpdateListeners()
    {
        if (ShouldListenToInput())
        {
            if (!this.listenerSubscribed)
            {
                this.inputEventSource.SubscribeToKeyDown(OnInputKeyDown);
                this.listenerSubscribed = true;
            }
        }
        else
        {
            if (this.listenerSubscribed)
            {
                this.inputEventSource.UnsubscribeToKeyDown(OnInputKeyDown);
                this.listenerSubscribed = false;
            }
        }
    }

    private bool ShouldListenToInput()
    {
        // Push-to-talk is handled in PushToTalkController.cs
        return this.configuration.MuteMicKeybind != default ||
            this.configuration.DeafenKeybind != default;
    }

    private void OnInputKeyDown(KeyDown k)
    {
        if (k.Key == this.configuration.MuteMicKeybind)
        {
            this.audioDeviceController.MuteMic = !this.audioDeviceController.MuteMic;
        }
        if (k.Key == this.configuration.DeafenKeybind)
        {
            this.audioDeviceController.Deafen = !this.audioDeviceController.Deafen;
        }
        this.voiceRoomManager.PushPlayerAudioState();
    }
}
