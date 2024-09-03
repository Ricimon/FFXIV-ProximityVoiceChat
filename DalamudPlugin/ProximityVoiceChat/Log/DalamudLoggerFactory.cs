using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;

namespace ProximityVoiceChat.Log;

public class DalamudLoggerFactory(IPluginLog pluginLog, IChatGui chatGui, Configuration configuration) : ILoggerFactory
{
    private readonly DalamudLogger logger = new(pluginLog, chatGui, configuration);

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return this.logger;
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}
