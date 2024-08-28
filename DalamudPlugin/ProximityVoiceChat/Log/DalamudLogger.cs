using Dalamud.Game.Text;
using Dalamud.Plugin.Services;

namespace ProximityVoiceChat.Log
{
    public class DalamudLogger : ILogger
    {
        private readonly IPluginLog pluginLog;
        private readonly IChatGui chatGui;
        private readonly Configuration configuration;

        public DalamudLogger(IPluginLog pluginLog, IChatGui chatGui, Configuration configuration)
        {
            this.pluginLog = pluginLog;
            this.chatGui = chatGui;
            this.configuration = configuration;
        }

        public void Trace(string message, params object[] values)
        {
            this.pluginLog.Verbose(message, values);
            Log(LogLevel.Trace, message, values);
        }

        public void Debug(string message, params object[] values)
        {
            this.pluginLog.Debug(message, values);
            Log(LogLevel.Debug, message, values);
        }

        public void Info(string message, params object[] values)
        {
            this.pluginLog.Info(message, values);
            Log(LogLevel.Info, message, values);
        }

        public void Warn(string message, params object[] values)
        {
            this.pluginLog.Warning(message, values);
            Log(LogLevel.Warn, message, values);
        }

        public void Error(string message, params object[] values)
        {
            this.pluginLog.Error(message, values);
            Log(LogLevel.Error, message, values);
        }

        public void Fatal(string message, params object[] values)
        {
            this.pluginLog.Fatal(message, values);
            Log(LogLevel.Fatal, message, values);
        }

        private void Log(LogLevel logLevel, string message, params object[] values)
        {
            if (!this.configuration.PrintLogsToChat)
            {
                return;
            }

            if (logLevel.Ordinal < this.configuration.MinimumVisibleLogLevel)
            {
                return;
            }

            XivChatType chatType;

            if (logLevel.Ordinal <= LogLevel.Warn.Ordinal)
            {
                chatType = XivChatType.Debug;
            }
            else
            {
                chatType = XivChatType.ErrorMessage;
            }

            this.chatGui.Print(new XivChatEntry
            {
                Message = $"{logLevel} | {string.Format(message, values)}",
                Type = chatType
            });
        }
    }
}
