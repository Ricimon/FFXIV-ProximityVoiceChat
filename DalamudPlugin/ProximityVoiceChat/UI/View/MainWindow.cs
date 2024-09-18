using Dalamud.Interface.Windowing;
using ImGuiNET;
using Reactive.Bindings;
using System;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.UI.Util;
using Microsoft.MixedReality.WebRTC;
using System.Text;
using Dalamud.Plugin.Services;
using Dalamud.Plugin;
using System.IO;

namespace ProximityVoiceChat.UI.View;

public class MainWindow : Window, IMainWindow, IDisposable
{
    // this extra bool exists for ImGui, since you can't ref a property
    private bool visible = false;
    public bool Visible
    {
        get => this.visible;
        set => this.visible = value;
    }

    public IReactiveProperty<bool> PublicRoom { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<string> RoomName { get; } = new ReactiveProperty<string>(string.Empty);
    public IReactiveProperty<string> RoomPassword { get; } = new ReactiveProperty<string>(string.Empty);

    private readonly Subject<Unit> joinVoiceRoom = new();
    public IObservable<Unit> JoinVoiceRoom => joinVoiceRoom.AsObservable();
    private readonly Subject<Unit> leaveVoiceRoom = new();
    public IObservable<Unit> LeaveVoiceRoom => leaveVoiceRoom.AsObservable();

    public IReactiveProperty<int> SelectedAudioInputDeviceIndex { get; } = new ReactiveProperty<int>(-1);
    public IReactiveProperty<int> SelectedAudioOutputDeviceIndex { get; } = new ReactiveProperty<int>(-1);
    public IReactiveProperty<bool> PlayingBackMicAudio { get; } = new ReactiveProperty<bool>();

    public IObservable<bool> MuteMic => muteMic.AsObservable();
    private readonly Subject<bool> muteMic = new();
    public IObservable<bool> Deafen => deafen.AsObservable();
    private readonly Subject<bool> deafen = new();

    public IReactiveProperty<float> MasterVolume { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<AudioFalloffModel.FalloffType> AudioFalloffType { get; } = new ReactiveProperty<AudioFalloffModel.FalloffType>();
    public IReactiveProperty<float> AudioFalloffMinimumDistance { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<float> AudioFalloffMaximumDistance { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<float> AudioFalloffFactor { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<bool> MuteDeadPlayers { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<bool> MuteOutOfMapPlayers { get; } = new ReactiveProperty<bool>();

    public IReactiveProperty<bool> PrintLogsToChat { get; } = new ReactiveProperty<bool>();
    public IReactiveProperty<int> MinimumVisibleLogLevel { get; } = new ReactiveProperty<int>();

    private string[]? inputDevices;
    private string[]? outputDevices;

    private readonly WindowSystem windowSystem;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ITextureProvider textureProvider;
    private readonly AudioDeviceController audioDeviceController;
    private readonly VoiceRoomManager voiceRoomManager;
    private readonly string[] falloffTypes;

    public MainWindow(
        WindowSystem windowSystem,
        IDalamudPluginInterface pluginInterface,
        ITextureProvider textureProvider,
        AudioDeviceController audioDeviceController,
        VoiceRoomManager voiceRoomManager) : base(
        PluginInitializer.Name)
    {
        this.windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
        this.pluginInterface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface));
        this.textureProvider = textureProvider ?? throw new ArgumentNullException(nameof(textureProvider));
        this.audioDeviceController = audioDeviceController ?? throw new ArgumentNullException(nameof(audioDeviceController));
        this.voiceRoomManager = voiceRoomManager ?? throw new ArgumentNullException(nameof(voiceRoomManager));
        this.falloffTypes = Enum.GetNames(typeof(AudioFalloffModel.FalloffType));
        windowSystem.AddWindow(this);
    }

    public override void Draw()
    {
        if (!Visible)
        {
            return;
        }

        var width = 350;
        ImGui.SetNextWindowSize(new Vector2(width, 400), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 250), new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("ProximityVoiceChat", ref this.visible))
        {
            DrawContents();
        }
        ImGui.End();
    }

    public void Dispose()
    {
        windowSystem.RemoveWindow(this);
        inputDevices = null;
        outputDevices = null;
        GC.SuppressFinalize(this);
    }

    private void DrawContents()
    {
        if(ImGui.BeginTable("JoinSettings", 2))
        {
            ImGui.TableSetupColumn("Public", ImGuiTableColumnFlags.WidthFixed, 155);
            ImGui.TableSetupColumn("Private", ImGuiTableColumnFlags.WidthFixed, 155);

            ImGui.TableNextColumn();
            if (ImGui.Selectable("Public room", this.PublicRoom.Value))
            {
                this.PublicRoom.Value = true;
            }
            ImGui.TableNextColumn();
            if (ImGui.Selectable("Private room", !this.PublicRoom.Value))
            {
                this.PublicRoom.Value = false;
            }

            ImGui.EndTable();
        }

        //var indent = 10;
        //ImGui.Indent(indent);
        if (this.PublicRoom.Value)
        {
            if (this.voiceRoomManager.InRoom) { ImGui.BeginDisabled(); }
            if (ImGui.Button("Join Public Voice Room"))
            {
                this.joinVoiceRoom.OnNext(Unit.Default);
            }
            if (this.voiceRoomManager.InRoom) { ImGui.EndDisabled(); }
        }
        else
        {
            string roomName = this.RoomName.Value;
            if (ImGui.InputText("Room Name", ref roomName, 100, ImGuiInputTextFlags.AutoSelectAll))
            {
                this.RoomName.Value = roomName;
            }
            ImGui.SameLine(); HelpMarker("Leave blank to join your own room");

            string roomPassword = this.RoomPassword.Value;
            ImGui.PushItemWidth(38);
            if (ImGui.InputText("Room Password (up to 4 digits)", ref roomPassword, 4, ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.AutoSelectAll))
            {
                this.RoomPassword.Value = roomPassword;
            }
            ImGui.PopItemWidth();
            if (!ImGui.IsItemActive())
            {
                while (roomPassword.Length < 4)
                {
                    roomPassword = "0" + roomPassword;
                }
                this.RoomPassword.Value = roomPassword;
            }
            ImGui.SameLine(); HelpMarker("Sets the password if joining your own room");

            if (this.voiceRoomManager.InRoom) { ImGui.BeginDisabled(); }
            if (ImGui.Button("Join Private Voice Room"))
            {
                this.joinVoiceRoom.OnNext(Unit.Default);
            }
            if (this.voiceRoomManager.InRoom) { ImGui.EndDisabled(); }
        }
        //ImGui.Indent(-indent);

        ImGui.Dummy(new Vector2(0.0f, 5.0f)); // ---------------

        var resourcesDir = Path.Combine(this.pluginInterface.AssemblyLocation.Directory?.FullName!, "Resources");
        var muteMic = this.audioDeviceController.MuteMic || this.audioDeviceController.Deafen;
        var microphoneImageName = muteMic ? "microphone-muted.png" : "microphone.png" ;
        var microphoneImage = this.textureProvider.GetFromFile(Path.Combine(resourcesDir, microphoneImageName)).GetWrapOrDefault();
        if (ImGui.ImageButton(microphoneImage?.ImGuiHandle ?? default, new Vector2(20, 20)))
        {
            this.muteMic.OnNext(!muteMic);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(muteMic ? "Turn On Microphone" : "Turn Off Microphone");
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        var headphonesImageName = this.audioDeviceController.Deafen ? "headphones-deafen.png" : "headphones.png";
        var headphonesImage = this.textureProvider.GetFromFile(Path.Combine(resourcesDir, headphonesImageName)).GetWrapOrDefault();
        if (ImGui.ImageButton(headphonesImage?.ImGuiHandle ?? default, new Vector2(20, 20)))
        {
            this.deafen.OnNext(!this.audioDeviceController.Deafen);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(this.audioDeviceController.Deafen ? "Undeafen" : "Deafen");
            ImGui.EndTooltip();
        }

        ImGui.Dummy(new Vector2(0.0f, 5.0f)); // ---------------

        if (this.voiceRoomManager.InRoom)
        {
            DrawVoiceRoom();
            ImGui.Dummy(new Vector2(0.0f, 5.0f)); // ---------------
        }

        if (ImGui.CollapsingHeader("Audio Devices"))
        {
            if (ImGui.BeginTable("AudioDevices", 2))
            {
                ImGui.TableSetupColumn("AudioDevicesCol1", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("AudioDevicesCol2", ImGuiTableColumnFlags.WidthFixed, 230);

                ImGui.TableNextRow(); ImGui.TableNextColumn();
                //var rightPad = 110;

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

                ImGui.EndTable();
            }
            if (ImGui.Button(this.PlayingBackMicAudio.Value ? "Stop Mic Playback" : "Test Mic Playback"))
            {
                this.PlayingBackMicAudio.Value = !this.PlayingBackMicAudio.Value;
            }
        }

        if (ImGui.CollapsingHeader("Audio Falloff Settings"))
        {
            if (ImGui.BeginTable("AudioFalloffSettings", 2))
            {
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

                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Mute Out Of Map Players");
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                if (this.voiceRoomManager.InPublicRoom)
                {
                    ImGui.BeginDisabled();
                }
                var muteOutOfMapPlayers = this.voiceRoomManager.InPublicRoom || this.MuteOutOfMapPlayers.Value;
                if (ImGui.Checkbox("##MuteOutOfMapPlayers", ref muteOutOfMapPlayers))
                {
                    this.MuteOutOfMapPlayers.Value = muteOutOfMapPlayers;
                }
                if (this.voiceRoomManager.InPublicRoom)
                {
                    ImGui.EndDisabled();
                }
                ImGui.SameLine(); HelpMarker("Can only disable in private rooms");

                ImGui.EndTable();
            }
        }

        if (ImGui.BeginTable("SeparatorLine1", 1, ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Spacing();

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

            ImGui.EndTable();
        }
    }

    private void DrawVoiceRoom()
    {
        ImGui.AlignTextToFramePadding();
        var roomName = this.voiceRoomManager.SignalingChannel?.RoomName;
        if (string.IsNullOrEmpty(roomName))
        {
            ImGui.Text("Public Voice Room");
        }
        else
        {
            ImGui.Text($"{roomName}'s Voice Room");
        }
        if (this.voiceRoomManager.InRoom)
        {
            ImGui.SameLine();
            if (ImGui.Button("Leave"))
            {
                this.leaveVoiceRoom.OnNext(Unit.Default);
            }
        }

        var indent = 10;
        ImGui.Indent(indent);

        foreach (var (playerName, index) in this.voiceRoomManager.PlayersInVoiceRoom.Select((p, i) => (p, i)))
        {
            Vector4 color = Vector4Colors.Red;
            string tooltip = "Connection Error";

            // Assume first player is always the local player
            if (index == 0)
            {
                var signalingChannel = this.voiceRoomManager.SignalingChannel;
                if (signalingChannel != null)
                {
                    if (signalingChannel.Connected)
                    {
                        color = Vector4Colors.Green;
                        tooltip = "Connected";
                    }
                    else if (!signalingChannel.Disconnected)
                    {
                        color = Vector4Colors.Orange;
                        tooltip = "Connecting";
                    }
                }
            }
            else
            {
                if (this.voiceRoomManager.WebRTCManager != null &&
                    this.voiceRoomManager.WebRTCManager.Peers.TryGetValue(playerName, out var peer))
                {
                    DataChannel? dataChannel = null;
                    if (peer.PeerConnection.DataChannels.Count > 0)
                    {
                        dataChannel = peer.PeerConnection.DataChannels[0];
                    }

                    if (dataChannel != null && dataChannel.State == DataChannel.ChannelState.Open)
                    {
                        color = Vector4Colors.Green;
                        tooltip = "Connected";
                    }
                    else if (dataChannel == null || dataChannel.State == DataChannel.ChannelState.Connecting)
                    {
                        color = Vector4Colors.Orange;
                        tooltip = "Connecting";
                    }
                }
            }

            // Connectivity indicator
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var h = ImGui.GetTextLineHeightWithSpacing();
            //pos += new Vector2(ImGui.GetWindowSize().X - 110, -h);
            var radius = 0.3f * h;
            pos += new Vector2(0, h / 2f);
            drawList.AddCircleFilled(pos, radius, ImGui.ColorConvertFloat4ToU32(color));
            if (Vector2.Distance(ImGui.GetMousePos(), pos) < radius)
            {
                ImGui.SetTooltip(tooltip);
            }
            pos += new Vector2(radius + 3, -h / 2.25f);
            ImGui.SetCursorScreenPos(pos);

            var playerLabel = new StringBuilder(playerName);
            if (index > 0 && this.voiceRoomManager.TrackedPlayers.TryGetValue(playerName, out var tp))
            {
                playerLabel.Append(" (");
                playerLabel.Append(float.IsNaN(tp.Distance) ? '?' : tp.Distance.ToString("F1"));
                playerLabel.Append($"y, {tp.Volume:F2})");
            }
            ImGui.Text(playerLabel.ToString());
        }

        ImGui.Indent(-indent);
    }

    private static void HelpMarker(string description)
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
