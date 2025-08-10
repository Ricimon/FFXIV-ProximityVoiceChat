using Dalamud.Bindings.ImGui;

namespace ProximityVoiceChat.UI.Util; 

public class Common 
{
    public static void HelpMarker(string description) 
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered()) 
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(description);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}