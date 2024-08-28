using Reactive.Bindings;
using System;
using System.Reactive;

namespace ProximityVoiceChat.UI.View;

public interface IMainWindow : IPluginUIView
{
    public IReactiveProperty<int> SelectedAudioInputDeviceIndex { get; }
    public IReactiveProperty<int> SelectedAudioOutputDeviceIndex { get; }
    public IReactiveProperty<bool> PlayingBackMicAudio { get; }

    public IObservable<Unit> JoinVoiceRoom { get; }
    public IObservable<Unit> LeaveVoiceRoom { get; }

    public IObservable<Unit> LogAllGameObjects { get; }

    public IReactiveProperty<string> AliveStateSourceName { get; }
    public IReactiveProperty<string> DeadStateSourceName { get; }

    public IReactiveProperty<bool> PrintLogsToChat { get; }
    public IReactiveProperty<int> MinimumVisibleLogLevel { get; }
}
