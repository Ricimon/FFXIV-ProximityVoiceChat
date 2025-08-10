using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ProximityVoiceChat.Input;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.UI.Util;
using Reactive.Bindings;
using WindowsInput.Events;

namespace ProximityVoiceChat.UI.View;

public class ConfigWindow
{
    public IReactiveProperty<int> SelectedAudioInputDeviceIndex { get; } = new ReactiveProperty<int>(-1);
    public IReactiveProperty<int> SelectedAudioOutputDeviceIndex { get; } = new ReactiveProperty<int>(-1);
    public IReactiveProperty<bool> PlayingBackMicAudio { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> PushToTalk { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> SuppressNoise { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<Keybind> KeybindBeingEdited { get; } = new ReactiveProperty<Keybind>();
    public IObservable<Keybind> ClearKeybind => clearKeybind.AsObservable();
    private readonly Subject<Keybind> clearKeybind = new();

    public IReactiveProperty<float> MasterVolume { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<AudioFalloffModel.FalloffType> AudioFalloffType { get; } = new ReactiveProperty<AudioFalloffModel.FalloffType>();
    public IReactiveProperty<float> AudioFalloffMinimumDistance { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<float> AudioFalloffMaximumDistance { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<float> AudioFalloffFactor { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<bool> EnableSpatialization { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> MuteDeadPlayers { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<int> MuteDeadPlayersDelayMs { get; } = new ReactiveProperty<int>();
    public IReactiveProperty<bool> UnmuteAllIfDead { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> MuteOutOfMapPlayers { get; } = new ReactiveProperty<bool>();

    public IReactiveProperty<bool> PlayRoomJoinAndLeaveSounds { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> KeybindsRequireGameFocus { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> PrintLogsToChat { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<int> MinimumVisibleLogLevel { get; } = new ReactiveProperty<int>();

    private string[]? inputDevices;
    private string[]? outputDevices;

    private readonly IAudioDeviceController audioDeviceController;
    private readonly VoiceRoomManager voiceRoomManager;
    private readonly Configuration configuration;
    private readonly string[] falloffTypes;
    private readonly string[] allLoggingLevels;

    // Direct application logic is being placed into this UI script because this is debug UI
    public ConfigWindow(PushToTalkController pushToTalkController,
        VoiceRoomManager voiceRoomManager,
        Configuration configuration)
    {
        this.audioDeviceController = pushToTalkController;
        this.voiceRoomManager = voiceRoomManager;
        this.configuration = configuration;
        this.falloffTypes = Enum.GetNames<AudioFalloffModel.FalloffType>();
        this.allLoggingLevels = [.. LogLevel.AllLoggingLevels.Select(l => l.Name)];
    }

    public void Draw(bool visible)
    {
        if (!visible)
        {
            KeybindBeingEdited.Value = Keybind.None;
            return;
        }
        DrawContents();
    }

    private void DrawContents()
    {
        using var tabs = ImRaii.TabBar("pvc-config-tabs");
        if (!tabs) return;

        DrawDeviceTab();
        DrawFalloffTab();
        DrawMiscTab();
    }

    private void DrawDeviceTab()
    {
        using var deviceTab = ImRaii.TabItem("Audio Devices");
        if (!deviceTab) return;

        using (var deviceTable = ImRaii.Table("AudioDevices", 2))
        {
            if (deviceTable)
            {
                ImGui.TableSetupColumn("AudioDevicesCol1", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("AudioDevicesCol2", ImGuiTableColumnFlags.WidthFixed, 230);

                ImGui.TableNextRow(); ImGui.TableNextColumn();

                ImGui.AlignTextToFramePadding();
                ImGui.Text("Input Device"); ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                this.inputDevices ??= this.audioDeviceController.GetAudioRecordingDevices().ToArray();
                var inputDeviceIndex = this.SelectedAudioInputDeviceIndex.Value + 1;
                if (ImGui.Combo("##InputDevice", ref inputDeviceIndex, this.inputDevices, this.inputDevices.Length))
                {
                    this.SelectedAudioInputDeviceIndex.Value = inputDeviceIndex - 1;
                }

                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Output Device"); ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                this.outputDevices ??= this.audioDeviceController.GetAudioPlaybackDevices().ToArray();
                var outputDeviceIndex = this.SelectedAudioOutputDeviceIndex.Value + 1;
                if (ImGui.Combo("##OutputDevice", ref outputDeviceIndex, this.outputDevices, this.outputDevices.Length))
                {
                    this.SelectedAudioOutputDeviceIndex.Value = outputDeviceIndex - 1;
                }
            }
        }

        if (ImGui.Button(this.PlayingBackMicAudio.Value ? "Stop Mic Playback" : "Test Mic Playback"))
        {
            this.PlayingBackMicAudio.Value = !this.PlayingBackMicAudio.Value;
        }

        var pushToTalk = this.PushToTalk.Value;
        if (ImGui.Checkbox("Push to Talk", ref pushToTalk))
        {
            this.PushToTalk.Value = pushToTalk;
        }
        if (pushToTalk)
        {
            ImGui.SameLine();
            DrawKeybindEdit(Keybind.PushToTalk, this.configuration.PushToTalkKeybind, "Keybind");
        }
        else if (this.KeybindBeingEdited.Value == Keybind.PushToTalk)
        {
            this.KeybindBeingEdited.Value = Keybind.None;
        }

        var suppressNoise = this.SuppressNoise.Value;
        if (ImGui.Checkbox("Suppress Noise", ref suppressNoise))
        {
            this.SuppressNoise.Value = suppressNoise;
        }

        ImGui.Dummy(new Vector2(0.0f, 5.0f)); // ---------------

        ImGui.Text("Keybinds");
        ImGui.SameLine(); Common.HelpMarker("Right click to clear a keybind.");
        using (ImRaii.PushIndent())
        {
            DrawKeybindEdit(Keybind.MuteMic, this.configuration.MuteMicKeybind, "Mute Microphone Keybind");
            DrawKeybindEdit(Keybind.Deafen, this.configuration.DeafenKeybind, "Deafen Keybind");
        }
    }

    private void DrawKeybindEdit(Keybind keybind, KeyCode currentBinding, string label)
    {
        using var id = ImRaii.PushId($"{keybind} Keybind");
        {
            if (ImGui.Button(this.KeybindBeingEdited.Value == keybind ?
                    "Recording..." :
                    KeyCodeStrings.TranslateKeyCode(currentBinding),
                new Vector2(5 * ImGui.GetFontSize(), 0)))
            {
                this.KeybindBeingEdited.Value = this.KeybindBeingEdited.Value != keybind ?
                    keybind : Keybind.None;
            }
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            this.clearKeybind.OnNext(keybind);
        }
        ImGui.SameLine();
        ImGui.Text(label);
    }

    private void DrawFalloffTab()
    {
        using var falloffTab = ImRaii.TabItem("Audio Falloff");
        if (!falloffTab) return;

        using (var falloffTable = ImRaii.Table("AudioFalloff", 2))
        {
            if (!falloffTable) return;

            ImGui.TableSetupColumn("AudioFalloffSettingsCol1", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("AudioFalloffSettingsCol2", ImGuiTableColumnFlags.WidthFixed, 150);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Master Volume"); ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            var masterVolume = this.MasterVolume.Value * 100.0f;
            if (ImGui.SliderFloat("##MasterVolume", ref masterVolume, 0.0f, 500.0f, "%1.0f%%"))
            {
                this.MasterVolume.Value = masterVolume / 100.0f;
            }

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Falloff Type"); ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            var falloffType = (int)this.AudioFalloffType.Value;
            if (ImGui.Combo("##AudioFalloffType", ref falloffType, this.falloffTypes, this.falloffTypes.Length))
            {
                this.AudioFalloffType.Value = (AudioFalloffModel.FalloffType)falloffType;
            }

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Minimum Distance");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Volume is max when below this distance, in yalms");
            }
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            var minDistance = this.AudioFalloffMinimumDistance.Value;
            if (ImGui.InputFloat("##AudioFalloffMinimumDistance", ref minDistance, 0.1f, 1.0f, "%.1f"))
            {
                this.AudioFalloffMinimumDistance.Value = minDistance;
            }

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Maximum Distance");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Volume is 0 when above this distance, in yalms");
            }
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            var maxDistance = this.AudioFalloffMaximumDistance.Value;
            if (ImGui.InputFloat("##AudioFalloffMaximumDistance", ref maxDistance, 0.1f, 1.0f, "%.1f"))
            {
                this.AudioFalloffMaximumDistance.Value = maxDistance;
            }

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Falloff Factor");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("The higher this number, the quicker volume drops off over distance. This value is not used in linear falloff.");
            }
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            var falloffFactor = this.AudioFalloffFactor.Value;
            if (ImGui.InputFloat("##AudioFalloffFactor", ref falloffFactor, 0.1f, 1.0f, "%.1f"))
            {
                this.AudioFalloffFactor.Value = falloffFactor;
            }

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Enable Spatialization");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Use camera facing direction to pan incoming audio");
            }
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            var enableSpatialization = this.EnableSpatialization.Value;
            if (ImGui.Checkbox("##EnableSpatialization", ref enableSpatialization))
            {
                this.EnableSpatialization.Value = enableSpatialization;
            }

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Mute Dead Players");
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            var muteDeadPlayers = this.MuteDeadPlayers.Value;
            if (ImGui.Checkbox("##MuteDeadPlayers", ref muteDeadPlayers))
            {
                this.MuteDeadPlayers.Value = muteDeadPlayers;
            }
            ImGui.SameLine();
            var muteDeadPlayersDelayMs = this.MuteDeadPlayersDelayMs.Value;
            ImGui.Text("Delay (ms)");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Delay before a just-died player is actually muted");
            }
            ImGui.SameLine(); ImGui.SetNextItemWidth(50);
            if (ImGui.InputInt("##Delay (ms)", ref muteDeadPlayersDelayMs, 0))
            {
                this.MuteDeadPlayersDelayMs.Value = muteDeadPlayersDelayMs;
            }

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Unmute All If Dead");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Hear everyone if you are dead, helps reduce loneliness on wipes");
            }
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            var unmuteAllIfDead = this.UnmuteAllIfDead.Value;
            if (ImGui.Checkbox("##UnmuteAllIfDead", ref unmuteAllIfDead))
            {
                this.UnmuteAllIfDead.Value = unmuteAllIfDead;
            }

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Mute Out Of Map Players");
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            ImGui.BeginDisabled(this.voiceRoomManager.InPublicRoom);
            var muteOutOfMapPlayers = this.voiceRoomManager.InPublicRoom || this.MuteOutOfMapPlayers.Value;
            if (ImGui.Checkbox("##MuteOutOfMapPlayers", ref muteOutOfMapPlayers))
            {
                this.MuteOutOfMapPlayers.Value = muteOutOfMapPlayers;
            }
            ImGui.EndDisabled();
            ImGui.SameLine(); Common.HelpMarker("Can only disable in private rooms");
        }
    }
    private void DrawMiscTab()
    {
        using var miscTab = ImRaii.TabItem("Misc");
        if (!miscTab) return;

        var playRoomJoinAndLeaveSounds = this.PlayRoomJoinAndLeaveSounds.Value;
        if (ImGui.Checkbox("Play room join and leave sounds", ref playRoomJoinAndLeaveSounds))
        {
            this.PlayRoomJoinAndLeaveSounds.Value = playRoomJoinAndLeaveSounds;
        }

        var keybindsRequireGameFocus = this.KeybindsRequireGameFocus.Value;
        if (ImGui.Checkbox("Keybinds require game focus", ref keybindsRequireGameFocus))
        {
            this.KeybindsRequireGameFocus.Value = keybindsRequireGameFocus;
        }

        var printLogsToChat = this.PrintLogsToChat.Value;
        if (ImGui.Checkbox("Print logs to chat", ref printLogsToChat))
        {
            this.PrintLogsToChat.Value = printLogsToChat;
        }

        if (printLogsToChat)
        {
            ImGui.SameLine();
            var minLogLevel = this.MinimumVisibleLogLevel.Value;
            ImGui.SetNextItemWidth(70);
            if (ImGui.Combo("Min log level", ref minLogLevel, allLoggingLevels, allLoggingLevels.Length))
            {
                this.MinimumVisibleLogLevel.Value = minLogLevel;
            }
        }

        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Bugs or suggestions?");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.40f, 0.95f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.41f, 0.45f, 1.0f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.32f, 0.36f, 0.88f, 1));
        if (ImGui.Button("Discord"))
        {
            Process.Start(new ProcessStartInfo { FileName = "https://discord.gg/rSucAJ6A7u", UseShellExecute = true });
        }
        ImGui.PopStyleColor(3);

        ImGui.SameLine();
        ImGui.Text("|");
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1.0f, 0.39f, 0.20f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1.0f, 0.49f, 0.30f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.92f, 0.36f, 0.18f, 1));
        if (ImGui.Button("Support on Ko-fi"))
        {
            Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/ricimon", UseShellExecute = true });
        }
        ImGui.PopStyleColor(3);
    }
}
