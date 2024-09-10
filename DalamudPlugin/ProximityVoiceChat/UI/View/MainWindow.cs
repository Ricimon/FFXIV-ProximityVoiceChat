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

    public IReactiveProperty<int> SelectedAudioInputDeviceIndex { get; } = new ReactiveProperty<int>(-1);
    public IReactiveProperty<int> SelectedAudioOutputDeviceIndex { get; } = new ReactiveProperty<int>(-1);

    public IReactiveProperty<bool> PlayingBackMicAudio { get; } = new ReactiveProperty<bool>();

    private readonly ISubject<Unit> joinVoiceRoom = new Subject<Unit>();
    public IObservable<Unit> JoinVoiceRoom => joinVoiceRoom.AsObservable();
    private readonly ISubject<Unit> leaveVoiceRoom = new Subject<Unit>();
    public IObservable<Unit> LeaveVoiceRoom => leaveVoiceRoom.AsObservable();

    public IReactiveProperty<float> MasterVolume { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<AudioFalloffModel.FalloffType> AudioFalloffType { get; } = new ReactiveProperty<AudioFalloffModel.FalloffType>();
    public IReactiveProperty<float> AudioFalloffMinimumDistance { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<float> AudioFalloffMaximumDistance { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<float> AudioFalloffFactor { get; } = new ReactiveProperty<float>();
    public IReactiveProperty<bool> MuteDeadPlayers { get; } = new ReactiveProperty<bool>();

    public IReactiveProperty<string> AliveStateSourceName { get; }
        = new ReactiveProperty<string>(string.Empty, ReactivePropertyMode.DistinctUntilChanged);
    public IReactiveProperty<string> DeadStateSourceName { get; }
        = new ReactiveProperty<string>(string.Empty, ReactivePropertyMode.DistinctUntilChanged);

    private readonly ISubject<Unit> testAlive = new Subject<Unit>();
    public IObservable<Unit> TestAlive => testAlive.AsObservable();
    private readonly ISubject<Unit> testDead = new Subject<Unit>();
    public IObservable<Unit> TestDead => testDead.AsObservable();

    public IReactiveProperty<bool> PrintLogsToChat { get; }
        = new ReactiveProperty<bool>(mode: ReactivePropertyMode.DistinctUntilChanged);
    public IReactiveProperty<int> MinimumVisibleLogLevel { get; }
        = new ReactiveProperty<int>(mode: ReactivePropertyMode.DistinctUntilChanged);

    private string[]? inputDevices;
    private string[]? outputDevices;

    private readonly WindowSystem windowSystem;
    private readonly AudioDeviceController audioDeviceController;
    private readonly VoiceRoomManager voiceRoomManager;
    private readonly string[] falloffTypes;

    public MainWindow(
        WindowSystem windowSystem,
        AudioDeviceController audioDeviceController,
        VoiceRoomManager voiceRoomManager) : base(
        PluginInitializer.Name)
    {
        this.windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
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

        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Voice Room"); // ---------------
        ImGui.SameLine();
        if (ImGui.Button(this.voiceRoomManager.InRoom ? "Leave" : "Join"))
        {
            if (this.voiceRoomManager.InRoom)
            {
                this.leaveVoiceRoom.OnNext(Unit.Default);
            }
            else
            {
                this.joinVoiceRoom.OnNext(Unit.Default);
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
        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Audio Falloff Settings"))
        {
            if (ImGui.BeginTable("AudioFalloffSettings", 2))
            {
                ImGui.TableSetupColumn("AudioFalloffSettingsCol1", ImGuiTableColumnFlags.WidthFixed, 110);
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
                ImGui.EndTable();
            }
        }

        //ImGui.Text("OBS Settings"); // ---------------
        //ImGui.Indent(indent);

        //if (ImGui.BeginTable("ObsSettings", 2))
        //{
        //    ImGui.TableSetupColumn("ObsSettingsCol1", ImGuiTableColumnFlags.WidthFixed, 150);
        //    ImGui.TableNextRow(); ImGui.TableNextColumn();
        //    var rightPad = 30;
        //    var aliveStateSourceName = this.AliveStateSourceName.Value;
        //    var aliveStateLabel = "Alive image source name";
        //    ImGui.Text(aliveStateLabel); ImGui.TableNextColumn();
        //    ImGui.SetNextItemWidth(ImGui.GetColumnWidth() - rightPad);
        //    if (ImGui.InputText($"##{aliveStateLabel}", ref aliveStateSourceName, 100))
        //    {
        //        this.AliveStateSourceName.Value = aliveStateSourceName;
        //    }
        //    ImGui.TableNextRow(); ImGui.TableNextColumn();
        //    var deadStateSourceName = this.DeadStateSourceName.Value;
        //    var deadStateLabel = "Dead image source name";
        //    ImGui.Text(deadStateLabel); ImGui.TableNextColumn();
        //    ImGui.SetNextItemWidth(ImGui.GetColumnWidth() - rightPad);
        //    if (ImGui.InputText($"##{deadStateLabel}", ref deadStateSourceName, 100))
        //    {
        //        this.DeadStateSourceName.Value = deadStateSourceName;
        //    }
        //    ImGui.EndTable();
        //}

        //ImGui.Indent(-indent);
        //ImGui.Spacing();

        //ImGui.Text("Debug"); // ---------------
        //ImGui.Indent(indent);

        //ImGui.Text("Character state:");
        //ImGui.SameLine();
        //ImGui.Text(this.IsCharacterAlive ? "Alive" : "Dead");
        //ImGuiExtensions.SetDisabled(!this.ObsConnected);
        //if (ImGui.Button("Test Alive"))
        //{
        //    this.testAlive.OnNext(Unit.Default);
        //}
        //ImGui.SameLine();
        //if (ImGui.Button("Test Dead"))
        //{
        //    this.testDead.OnNext(Unit.Default);
        //}
        //ImGuiExtensions.SetDisabled(false);
        //ImGui.SameLine();
        //ImGui.Text("(OBS only)");

        //ImGui.Indent(-indent);
        //ImGui.Spacing();

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
}
