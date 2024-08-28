using Dalamud.Configuration;
using Dalamud.Plugin;
using NLog;
using System;

namespace ProximityVoiceChat
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public int SelectedAudioInputDeviceIndex { get; set; } = -1;
        public int SelectedAudioOutputDeviceIndex { get; set; } = -1;

        public bool PrintLogsToChat { get; set; } = true;

        public int MinimumVisibleLogLevel { get; set; } = LogLevel.Info.Ordinal;

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
