using Reactive.Bindings;
using System;
using System.Reactive;

namespace ProximityVoiceChat.UI.View;

public interface IMainWindow : IPluginUIView
{
    public IReactiveProperty<bool> PublicRoom { get; }
    public IReactiveProperty<string> RoomName { get; }
    public IReactiveProperty<string> RoomPassword { get; }

    public IObservable<Unit> JoinVoiceRoom { get; }
    public IObservable<Unit> LeaveVoiceRoom { get; }

    public IReactiveProperty<int> SelectedAudioInputDeviceIndex { get; }
    public IReactiveProperty<int> SelectedAudioOutputDeviceIndex { get; }
    public IReactiveProperty<bool> PlayingBackMicAudio { get; }
    public IReactiveProperty<bool> PushToTalk { get; }
    public IReactiveProperty<bool> EditingPushToTalkKeybind { get; }
    public IReactiveProperty<bool> SuppressNoise { get; }

    public IObservable<bool> MuteMic { get; }
    public IObservable<bool> Deafen { get; }

    public IReactiveProperty<float> MasterVolume { get; }
    public IReactiveProperty<AudioFalloffModel.FalloffType> AudioFalloffType { get; }
    public IReactiveProperty<float> AudioFalloffMinimumDistance { get; }
    public IReactiveProperty<float> AudioFalloffMaximumDistance { get; }
    public IReactiveProperty<float> AudioFalloffFactor { get; }
    public IReactiveProperty<bool> MuteDeadPlayers { get; }
    public IReactiveProperty<bool> MuteOutOfMapPlayers { get; }

    public IReactiveProperty<bool> PrintLogsToChat { get; }
    public IReactiveProperty<int> MinimumVisibleLogLevel { get; }
}
