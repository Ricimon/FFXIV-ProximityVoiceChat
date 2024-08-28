using System;

namespace ProximityVoiceChat;

public interface IDalamudHook : IDisposable
{
    void HookToDalamud();
}
