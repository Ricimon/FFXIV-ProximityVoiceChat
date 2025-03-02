using Dalamud.Interface.Windowing;
using ImGuiNET;
using Reactive.Bindings;
using System;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ProximityVoiceChat.UI.Util;
using Microsoft.MixedReality.WebRTC;
using System.Text;
using Dalamud.Plugin.Services;
using Dalamud.Plugin;
using System.IO;
using ProximityVoiceChat.WebRTC;
using ProximityVoiceChat.Input;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Utility;
using Dalamud.Interface;
using ProximityVoiceChat.UI.Presenter;
using ProximityVoiceChat.Extensions;

namespace ProximityVoiceChat.UI.View;

public class MainWindow : Window, IPluginUIView, IDisposable
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

    public IObservable<bool> MuteMic => muteMic.AsObservable();
    private readonly Subject<bool> muteMic = new();
    public IObservable<bool> Deafen => deafen.AsObservable();
    private readonly Subject<bool> deafen = new();

    public IObservable<(string playerName, float volume)> SetPeerVolume => setPeerVolume.AsObservable();
    private readonly Subject<(string playerName, float volume)> setPeerVolume = new();

    private readonly WindowSystem windowSystem;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ITextureProvider textureProvider;
    private readonly PushToTalkController pushToTalkController;
    private readonly IAudioDeviceController audioDeviceController;
    private readonly VoiceRoomManager voiceRoomManager;
    private readonly MapManager mapChangeHandler;
    private readonly Configuration configuration;
    private readonly ConfigWindowPresenter configWindowPresenter;
    private readonly IClientState clientState;

    private string? createPrivateRoomButtonText;

    public MainWindow(
        WindowSystem windowSystem,
        IDalamudPluginInterface pluginInterface,
        ITextureProvider textureProvider,
        PushToTalkController pushToTalkController,
        VoiceRoomManager voiceRoomManager,
        MapManager mapChangeHandler,
        Configuration configuration,
        ConfigWindowPresenter configWindowPresenter, 
        IClientState clientState) : base(
        PluginInitializer.Name)
    {
        this.windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
        this.pluginInterface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface));
        this.textureProvider = textureProvider ?? throw new ArgumentNullException(nameof(textureProvider));
        this.pushToTalkController = pushToTalkController ?? throw new ArgumentNullException(nameof(pushToTalkController));
        this.audioDeviceController = pushToTalkController;
        this.voiceRoomManager = voiceRoomManager ?? throw new ArgumentNullException(nameof(voiceRoomManager));
        this.mapChangeHandler = mapChangeHandler ?? throw new ArgumentNullException(nameof(mapChangeHandler));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.configWindowPresenter = configWindowPresenter ?? throw new ArgumentNullException(nameof(configWindowPresenter));
        this.clientState = clientState ?? throw new ArgumentNullException(nameof(clientState));
        windowSystem.AddWindow(this);
    }

    public override void Draw()
    {
        if (!Visible)
        {
            this.createPrivateRoomButtonText = null;
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
        GC.SuppressFinalize(this);
    }

    private void DrawContents()
    {
        using var tabs = ImRaii.TabBar("pvc-tabs");
        if (!tabs) return;

        using (var iconFont = ImRaii.PushFont(UiBuilder.IconFont))
        {
            var gearIcon = FontAwesomeIcon.Cog.ToIconString();
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X - ImGuiHelpers.GetButtonSize(gearIcon).X);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 5);
            if (ImGui.Button(gearIcon)) this.configWindowPresenter.View.Visible = true;
        }

        if (ImGui.IsItemHovered()) 
        {
            ImGui.SetTooltip("Configuration");
        }

        DrawPublicTab();
        DrawPrivateTab();

        //var indent = 10;
        //ImGui.Indent(indent);

        //ImGui.Indent(-indent);

        ImGui.Dummy(new Vector2(0.0f, 5.0f)); // ---------------

        if (ImGui.ImageButton(GetMicrophoneImage(this.audioDeviceController.MuteMic, true), new Vector2(20, 20)))
        {
            this.muteMic.OnNext(!this.audioDeviceController.MuteMic);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(this.audioDeviceController.MuteMic ? "Unmute Microphone" : "Mute Microphone");
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        if (ImGui.ImageButton(GetHeadphonesImage(this.audioDeviceController.Deafen, true), new Vector2(20, 20)))
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

    }

    private void DrawPublicTab() 
    {
        using var publicTab = ImRaii.TabItem("Public room");
        if (!publicTab) return;

        this.PublicRoom.Value = true;

        ImGui.BeginDisabled(!this.voiceRoomManager.ShouldBeInRoom);
        ImGui.Text(string.Format("Room ID: {0}", this.mapChangeHandler.GetCurrentMapPublicRoomName()));
        ImGui.EndDisabled();

#if DEBUG
        unsafe
        {
            ImGui.Text(string.Format("(DEBUG) Territory type: {0}", ((TerritoryIntendedUseEnum)FFXIVClientStructs.FFXIV.Client.Game.GameMain.Instance()->CurrentTerritoryIntendedUseId).ToString()));
        }
#endif

        ImGui.BeginDisabled(this.voiceRoomManager.ShouldBeInRoom);
        if (ImGui.Button("Join Public Voice Room")) 
        {
            this.joinVoiceRoom.OnNext(Unit.Default);
        }
        ImGui.EndDisabled();

        var dcMsg = this.voiceRoomManager.SignalingChannel?.LatestServerDisconnectMessage;
        if (dcMsg != null)
        {
            ImGui.SameLine();
            using var c = ImRaii.PushColor(ImGuiCol.Text, Vector4Colors.Red);
            ImGui.Text("Unknown error (see /xllog)");
        }
    }

    private void DrawPrivateTab() 
    {
        using var privateTab = ImRaii.TabItem("Private room");
        if (!privateTab) return;

        this.PublicRoom.Value = false;

        ImGuiInputTextFlags readOnlyIfInRoom = this.voiceRoomManager.InRoom ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None;
        string roomName = this.RoomName.Value;
         
        if (ImGui.InputText("Room Name", ref roomName, 100, ImGuiInputTextFlags.AutoSelectAll | readOnlyIfInRoom)) 
        {
            this.RoomName.Value = roomName;
        }
        ImGui.SameLine(); Common.HelpMarker("Leave blank to join your own room");

        string roomPassword = this.RoomPassword.Value;
        ImGui.PushItemWidth(38);
        if (ImGui.InputText("Room Password (up to 4 digits)", ref roomPassword, 4, ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.AutoSelectAll | readOnlyIfInRoom))
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
        ImGui.SameLine(); Common.HelpMarker("Sets the password if joining your own room");

        ImGui.BeginDisabled(this.voiceRoomManager.InRoom);
        if (this.createPrivateRoomButtonText == null || !this.voiceRoomManager.InRoom)
        {
            var playerName = this.clientState.GetLocalPlayerFullName();
            this.createPrivateRoomButtonText = roomName.Length == 0 || roomName == playerName ?
                "Create Private Voice Room" : "Join Private Voice Room";
        }
        if (ImGui.Button(this.createPrivateRoomButtonText))
        {
            this.joinVoiceRoom.OnNext(Unit.Default);
        }
        ImGui.EndDisabled();

        var dcMsg = this.voiceRoomManager.SignalingChannel?.LatestServerDisconnectMessage;
        if (dcMsg != null)
        {
            ImGui.SameLine();
            using var c = ImRaii.PushColor(ImGuiCol.Text, Vector4Colors.Red);
            // this is kinda scuffed but will do for now
            if (dcMsg.Contains("incorrect password"))
            {
                ImGui.Text("Incorrect password");
            }
            else if (dcMsg.Contains("room does not exist"))
            {
                ImGui.Text("Room not found");
            }
            else
            {
                ImGui.Text("Unknown error (see /xllog)");
            }
        }
    }

    private void DrawVoiceRoom()
    {
        ImGui.AlignTextToFramePadding();
        var roomName = this.voiceRoomManager.SignalingChannel?.RoomName;
        if (string.IsNullOrEmpty(roomName) || roomName.StartsWith("public"))
        {
            ImGui.Text("Public Voice Room");
        }
        else
        {
            ImGui.Text($"{roomName}'s Voice Room");
        }
        if (this.voiceRoomManager.ShouldBeInRoom)
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
            bool connected = false;
            Peer? peer = null;

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
                        connected = true;
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
                    this.voiceRoomManager.WebRTCManager.Peers.TryGetValue(playerName, out peer))
                {
                    if (peer.IceConnectionState != IceConnectionState.Closed)
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
                            connected = true;
                        }
                        else if (dataChannel == null || dataChannel.State == DataChannel.ChannelState.Connecting)
                        {
                            color = Vector4Colors.Orange;
                            tooltip = "Connecting";
                        }
                    }
                }
            }

            // Highlight row on hover
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var h = ImGui.GetTextLineHeightWithSpacing();
            var rowMin = new Vector2(ImGui.GetWindowPos().X, pos.Y);
            var rowMax = new Vector2(rowMin.X + ImGui.GetWindowWidth(), pos.Y + h);
            if (ImGui.IsMouseHoveringRect(rowMin, rowMax))
            {
                drawList.AddRectFilled(rowMin, rowMax, ImGui.ColorConvertFloat4ToU32(Vector4Colors.Gray));
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) || ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                {
                    ImGui.OpenPopup($"peer-menu-{index}");
                }
            }
            using (var popup = ImRaii.Popup($"peer-menu-{index}"))
            {
                if (popup)
                {
                    ImGui.Text(playerName);
                    using var d = ImRaii.Disabled(index == 0);
                    if (!this.configuration.PeerVolumes.TryGetValue(playerName, out var v))
                    {
                        v = 1.0f;
                    }
                    v *= 100.0f;
                    if (ImGui.SliderFloat("Volume", ref v, 0.0f, 200.0f, "%1.0f%%"))
                    {
                        this.setPeerVolume.OnNext((playerName, v / 100.0f));
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        this.setPeerVolume.OnNext((playerName, 1.0f));
                    }
                    if (index == 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.EndDisabled();
                        using var t = ImRaii.Tooltip();
                        if (t)
                        {
                            ImGui.Text("You can't change your own volume");
                        }
                        ImGui.BeginDisabled(index == 0);
                    }

                    if (index != 0)
                    {
                        using (var iconFont = ImRaii.PushFont(UiBuilder.IconFont))
                        {
                            var gearIcon = FontAwesomeIcon.Lightbulb.ToIconString();
                            ImGui.SameLine();
                            ImGui.SetWindowFontScale(0.75f);
                            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 0.25f * ImGui.GetTextLineHeight());
                            ImGui.Text(gearIcon);
                            ImGui.SetWindowFontScale(1.0f);
                        }
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        {
                            using var t = ImRaii.Tooltip();
                            if (t)
                            {
                                ImGui.TextUnformatted("Right click to reset to 100% volume");
                            }
                        }
                    }
                }
            }

            // Connectivity/activity indicator
            var radius = 0.3f * h;
            pos += new Vector2(0, h / 2f);
            if (index == 0)
            {
                if (connected &&
                    !this.audioDeviceController.PlayingBackMicAudio &&
                    (this.configuration.PushToTalk ?
                        this.pushToTalkController.PushToTalkKeyDown :
                        this.audioDeviceController.RecordingDataHasActivity))
                {
                    drawList.AddCircleFilled(pos, radius, ImGui.ColorConvertFloat4ToU32(color));
                }
                else
                {
                    drawList.AddCircle(pos, radius, ImGui.ColorConvertFloat4ToU32(color));
                }
            }
            else
            {
                if (connected &&
                    this.audioDeviceController.ChannelHasActivity(playerName))
                {
                    drawList.AddCircleFilled(pos, radius, ImGui.ColorConvertFloat4ToU32(color));
                }
                else
                {
                    drawList.AddCircle(pos, radius, ImGui.ColorConvertFloat4ToU32(color));
                }
            }
            // Tooltip
            if (Vector2.Distance(ImGui.GetMousePos(), pos) < radius)
            {
                ImGui.SetTooltip(tooltip);
            }
            pos += new Vector2(radius + 3, -h / 2.25f);
            ImGui.SetCursorScreenPos(pos);

            // Player Label
            var playerLabel = new StringBuilder(playerName);
            if (index > 0 && this.voiceRoomManager.TrackedPlayers.TryGetValue(playerName, out var tp))
            {
                playerLabel.Append(" (");
                playerLabel.Append(float.IsNaN(tp.Distance) ? '?' : tp.Distance.ToString("F1"));
                playerLabel.Append($"y, {tp.Volume:F2})");
            }
            ImGui.Text(playerLabel.ToString());

            // Muted/Deafened icons
            var iconSize = 0.9f * new Vector2(ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight());
            void DrawMicMutedIcon()
            {
                ImGui.SameLine();
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 0.5f * (ImGui.GetTextLineHeight() - iconSize.Y));
                ImGui.Image(GetMicrophoneImage(true, false), iconSize);
            }
            void DrawDeafenedIcon()
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 0.2f * iconSize.X);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 0.5f * (ImGui.GetTextLineHeight() - iconSize.Y));
                ImGui.Image(GetHeadphonesImage(true, false),  iconSize);
            }
            if (index == 0)
            {
                if (this.audioDeviceController.MuteMic)
                {
                    DrawMicMutedIcon();
                }
                if (this.audioDeviceController.Deafen)
                {
                    DrawDeafenedIcon();
                }
            }
            else if (connected)
            {
                if (peer?.AudioState.HasFlag(Peer.AudioStateFlags.MicMuted) ?? false)
                {
                    DrawMicMutedIcon();
                }
                if (peer?.AudioState.HasFlag(Peer.AudioStateFlags.Deafened) ?? false)
                {
                    DrawDeafenedIcon();
                }
            }
        }

        ImGui.Indent(-indent);
    }

    private nint GetMicrophoneImage(bool muted, bool self)
    {
        var imageName = muted ? (self ? "microphone-muted-self.png" : "microphone-muted.png") : "microphone.png";
        return this.textureProvider.GetFromFile(this.pluginInterface.GetResourcePath(imageName)).GetWrapOrDefault()?.ImGuiHandle ?? default;
    }

    private nint GetHeadphonesImage(bool deafened, bool self)
    {
        var imageName = deafened ? (self ? "headphones-deafen-self.png" : "headphones-deafen.png") : "headphones.png";
        return this.textureProvider.GetFromFile(this.pluginInterface.GetResourcePath(imageName)).GetWrapOrDefault()?.ImGuiHandle ?? default;
    }
}
