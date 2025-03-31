using Dalamud.Configuration;
using Dalamud.Plugin;
using NLog;
using System;
using System.Collections.Generic;
using WindowsInput.Events;

namespace ProximityVoiceChat
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        // Saved UI inputs
        public bool PublicRoom { get; set; }
        public string RoomName { get; set; } = string.Empty;
        public string RoomPassword { get; set; } = string.Empty;

        public int SelectedAudioInputDeviceIndex { get; set; } = -1;
        public int SelectedAudioOutputDeviceIndex { get; set; } = -1;
        public bool PushToTalk { get; set; }
        public KeyCode PushToTalkKeybind { get; set; }
        public int PushToTalkReleaseDelayMs { get; set; } = 20;
        public bool SuppressNoise { get; set; } = true;

        public bool MuteMic { get; set; }
        public bool Deafen { get; set; }
        public KeyCode MuteMicKeybind { get; set; }
        public KeyCode DeafenKeybind { get; set; }

        public float MasterVolume { get; set; } = 2.0f;

        public AudioFalloffModel FalloffModel { get; set; } = new();
        public bool MuteDeadPlayers { get; set; } = true;
        public int MuteDeadPlayersDelayMs { get; set; } = 2000;
        public bool MuteOutOfMapPlayers { get; set; } = false;
        public bool EnableSpatializationExperimental { get; set; } = false;

        public bool PlayRoomJoinAndLeaveSounds { get; set; } = true;
        public bool KeybindsRequireGameFocus { get; set; }
        public bool PrintLogsToChat { get; set; }

        public int MinimumVisibleLogLevel { get; set; } = LogLevel.Info.Ordinal;

        public Dictionary<string, float> PeerVolumes { get; set; } = [];

        // the below exist just to make saving less cumbersome
        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface!.SavePluginConfig(this);
        }
    }
}
