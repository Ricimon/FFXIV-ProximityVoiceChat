using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using ProximityVoiceChat.Input;
using ProximityVoiceChat.UI.Util;
using ProximityVoiceChat.Log;
using Reactive.Bindings;
using System;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Linq;

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


    public IReactiveProperty<int> SelectedAudioInputDeviceIndex { get; } = new ReactiveProperty<int>(-1);
    public IReactiveProperty<int> SelectedAudioOutputDeviceIndex { get; } = new ReactiveProperty<int>(-1);
    public IReactiveProperty<bool> PlayingBackMicAudio { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> PushToTalk { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> EditingPushToTalkKeybind { get; } = new ReactiveProperty<bool>();
    private readonly Subject<Unit> clearPushToTalkKeybind = new();
    public IObservable<Unit> ClearPushToTalkKeybind => clearPushToTalkKeybind.AsObservable();
    public IReactiveProperty<bool> SuppressNoise { get; } = new ReactiveProperty<bool>();


    public IReactiveProperty<float> MasterVolume { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<AudioFalloffModel.FalloffType> AudioFalloffType { get; } = new ReactiveProperty<AudioFalloffModel.FalloffType>();
    public IReactiveProperty<float> AudioFalloffMinimumDistance { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<float> AudioFalloffMaximumDistance { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<float> AudioFalloffFactor { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<bool> MuteDeadPlayers { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<int> MuteDeadPlayersDelayMs { get; } = new ReactiveProperty<int>();
    public IReactiveProperty<bool> MuteOutOfMapPlayers { get; } = new ReactiveProperty<bool>();

    public IReactiveProperty<bool> PrintLogsToChat { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<int> MinimumVisibleLogLevel { get; } = new ReactiveProperty<int>();

    private string[]? inputDevices;
    private string[]? outputDevices;

    private readonly WindowSystem windowSystem;
    private readonly IAudioDeviceController audioDeviceController;
    private readonly VoiceRoomManager voiceRoomManager;
    private readonly Configuration configuration;
    private readonly string[] falloffTypes;

    // Direct application logic is being placed into this UI script because this is debug UI
    public ConfigWindow(WindowSystem windowSystem,
        PushToTalkController pushToTalkController,
        VoiceRoomManager voiceRoomManager,
        Configuration configuration) : base(
        $"{PluginInitializer.Name} Config")
    {
        this.windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
        this.audioDeviceController = pushToTalkController ?? throw new ArgumentNullException(nameof(pushToTalkController));
        this.voiceRoomManager = voiceRoomManager ?? throw new ArgumentNullException(nameof(voiceRoomManager));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.falloffTypes = Enum.GetNames(typeof(AudioFalloffModel.FalloffType));
        windowSystem.AddWindow(this);
    }

    public override void Draw()
    {
        if (!Visible)
        {
            EditingPushToTalkKeybind.Value = false;
            return;
        }

        var minWindowSize = new Vector2(350, 400);
        ImGui.SetNextWindowSize(new Vector2(350, 400), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(350, 250), new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("ProximityVoiceChat Config", ref this.visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            DrawContents();
        }
        ImGui.End();
    }

    public void Dispose()
    {
        inputDevices = null;
        outputDevices = null;
        windowSystem.RemoveWindow(this);
        GC.SuppressFinalize(this);
    }

    private void DrawContents()
    {
        using var tabs = ImRaii.TabBar("pvc-config-tabs");
        if (!tabs) return;

        DrawGeneralTab();
        DrawDeviceTab();
        DrawFalloffTab();
    }

    private void DrawGeneralTab()
    {
        using var generalTab = ImRaii.TabItem("General");
        if (!generalTab) return;

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
            if (ImGui.Combo("Min log level",
                ref minLogLevel,
                LogLevel.AllLoggingLevels.Select(l => l.Name).ToArray(),
                LogLevel.AllLoggingLevels.Count()))
            {
                this.MinimumVisibleLogLevel.Value = minLogLevel;
            }
        }

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
            if (ImGui.Button(this.EditingPushToTalkKeybind.Value ?
                    "Recording..." :
                    KeyCodeStrings.TranslateKeyCode(this.configuration.PushToTalkKeybind),
                new Vector2(5 * ImGui.GetFontSize(), 0)))
            {
                this.EditingPushToTalkKeybind.Value = !this.EditingPushToTalkKeybind.Value;
            }
            if (ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
            {
                this.clearPushToTalkKeybind.OnNext(Unit.Default);
            }
            ImGui.SameLine();
            ImGui.Text("Keybind");
        } else
        {
            this.EditingPushToTalkKeybind.Value = false;
        }

        var suppressNoise = this.SuppressNoise.Value;
        if (ImGui.Checkbox("Suppress Noise", ref suppressNoise))
        {
            this.SuppressNoise.Value = suppressNoise;
        }
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
            ImGui.SameLine(); ImGui.SetNextItemWidth(50);
            if (ImGui.InputInt("##Delay (ms)", ref muteDeadPlayersDelayMs, 0))
            {
                this.MuteDeadPlayersDelayMs.Value = muteDeadPlayersDelayMs;
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
            ImGui.SameLine(); Util.Common.HelpMarker("Can only disable in private rooms");
        }
    }
}
