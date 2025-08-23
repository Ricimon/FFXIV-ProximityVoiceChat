using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using ProximityVoiceChat.UI.Presenter;

namespace ProximityVoiceChat;

public sealed class CommandDispatcher(
    ICommandManager commandManager,
    MainWindowPresenter mainWindowPresenter) : IDalamudHook
{
    private const string commandName = "/proximityvoicechat";
    private const string commandNameAlt = "/pvc";

    private readonly ICommandManager commandManager = commandManager;
    private readonly MainWindowPresenter mainWindowPresenter = mainWindowPresenter;

    public void HookToDalamud()
    {
        this.commandManager.AddHandler(commandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the ProximityVoiceChat window"
        });
        this.commandManager.AddHandler(commandNameAlt, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the ProximityVoiceChat window"
        });
    }

    public void Dispose()
    {
        this.commandManager.RemoveHandler(commandName);
        this.commandManager.RemoveHandler(commandNameAlt);
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just display our main ui
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        this.mainWindowPresenter.View.Visible = true;
    }
}
