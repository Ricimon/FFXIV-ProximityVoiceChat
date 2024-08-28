using ImGuiNET;

namespace ProximityVoiceChat;

public static class ImGuiExtensions
{
    public static void SetDisabled(bool disabled = true)
    {
        ImGui.GetStyle().Alpha = disabled ? 0.5f : 1.0f;
    }
}
