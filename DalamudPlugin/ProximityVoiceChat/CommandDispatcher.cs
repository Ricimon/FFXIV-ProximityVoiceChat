using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using System;
using ProximityVoiceChat.UI.Presenter;

namespace ProximityVoiceChat;

public class CommandDispatcher(
    ICommandManager commandManager,
    MainWindowPresenter mainWindowPresenter,
    ConfigWindowPresenter configWindowPresenter) : IDalamudHook
{
    private const string commandName = "/proximityvoicechat";
    private const string commandNameAlt = "/pvc";
    private const string configCommandName = "/proximityvoicechatconfig";

    private readonly ICommandManager commandManager = commandManager ?? throw new ArgumentNullException(nameof(commandManager));
    private readonly MainWindowPresenter mainWindowPresenter = mainWindowPresenter ?? throw new ArgumentNullException(nameof(mainWindowPresenter));
    private readonly ConfigWindowPresenter configWindowPresenter = configWindowPresenter ?? throw new ArgumentNullException(nameof(configWindowPresenter));

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
#if DEBUG
        this.commandManager.AddHandler(configCommandName, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = "Open the ProximityVoiceChat config window"
        });
#endif
    }

    public void Dispose()
    {
        this.commandManager.RemoveHandler(commandName);
#if DEBUG
        this.commandManager.RemoveHandler(configCommandName);
#endif
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

    private void OnConfigCommand(string command, string args)
    {
        this.configWindowPresenter.View.Visible = true;
    }
}
