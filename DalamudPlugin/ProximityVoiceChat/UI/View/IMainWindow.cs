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

    public IObservable<bool> MuteMic { get; }
    public IObservable<bool> Deafen { get; }

}
