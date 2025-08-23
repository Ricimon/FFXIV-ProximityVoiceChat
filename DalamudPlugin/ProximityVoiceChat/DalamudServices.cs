using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace ProximityVoiceChat;

public class DalamudServices
{
#pragma warning disable CA1822 // Mark members as static
    public IDalamudPluginInterface PluginInterface => PluginInitializer.PluginInterface;
    public ICommandManager CommandManager => PluginInitializer.CommandManager;
    public IClientState ClientState => PluginInitializer.ClientState;
    public IChatGui ChatGui => PluginInitializer.ChatGui;
    public ICondition Condition => PluginInitializer.Condition;
    public IDutyState DutyState => PluginInitializer.DutyState;
    public IDataManager DataManager => PluginInitializer.DataManager;
    public IObjectTable ObjectTable => PluginInitializer.ObjectTable;
    public IGameGui GameGui => PluginInitializer.GameGui;
    public IAddonEventManager AddonEventManager => PluginInitializer.AddonEventManager;
    public IAddonLifecycle AddonLifecycle => PluginInitializer.AddonLifecycle;
    public IFramework Framework => PluginInitializer.Framework;
    public ITextureProvider TextureProvider => PluginInitializer.TextureProvider;
    public IPluginLog Log => PluginInitializer.Log;
#pragma warning restore CA1822 // Mark members as static
}
