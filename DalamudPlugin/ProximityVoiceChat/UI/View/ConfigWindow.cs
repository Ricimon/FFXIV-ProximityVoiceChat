using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using System;
using System.Numerics;

namespace ProximityVoiceChat.UI.View;

public class ConfigWindow : Window, IPluginUIView, IDisposable
{
    // this extra bool exists for ImGui, since you can't ref a property
    private bool visible = false;
    public bool Visible
    {
        get => this.visible;
        set => this.visible = value;
    }

    private readonly WindowSystem windowSystem;
    private readonly IChatGui chatGui;

    // Direct application logic is being placed into this UI script because this is debug UI
    public ConfigWindow(WindowSystem windowSystem, IChatGui chatGui) : base(
        $"{PluginInitializer.Name} Config")
    {
        this.windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
        this.chatGui = chatGui ?? throw new ArgumentNullException(nameof(chatGui));

        windowSystem.AddWindow(this);
    }

    public override void Draw()
    {
        if (!Visible)
        {
            return;
        }

        var minWindowSize = new Vector2(375, 330);
        ImGui.SetNextWindowSize(minWindowSize, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(minWindowSize, new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("ProximityVoiceChat Config", ref this.visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            DrawContents();
        }
        ImGui.End();
    }

    public void Dispose()
    {
        windowSystem.RemoveWindow(this);
    }

    private void DrawContents()
    {
        if (ImGui.Button("Echo some text"))
        {
            chatGui.Print(new XivChatEntry
            {
                Message = "Test",
                Type = XivChatType.Debug
            });
        }
        if (ImGui.Button("Echo some error text"))
        {
            chatGui.Print(new XivChatEntry
            {
                Message = "Error",
                Type = XivChatType.ErrorMessage
            });
        }
    }
}
