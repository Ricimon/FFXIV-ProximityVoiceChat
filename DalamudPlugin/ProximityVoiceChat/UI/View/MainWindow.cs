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

    public IReactiveProperty<int> SelectedAudioInputDeviceIndex { get; init; } = new ReactiveProperty<int>(-1);
    public IReactiveProperty<int> SelectedAudioOutputDeviceIndex { get; init; } = new ReactiveProperty<int>(-1);

    public IReactiveProperty<bool> PlayingBackMicAudio { get; init; } = new ReactiveProperty<bool>();

    private readonly ISubject<Unit> joinVoiceRoom = new Subject<Unit>();
    public IObservable<Unit> JoinVoiceRoom => joinVoiceRoom.AsObservable();
    private readonly ISubject<Unit> leaveVoiceRoom = new Subject<Unit>();
    public IObservable<Unit> LeaveVoiceRoom => leaveVoiceRoom.AsObservable();

    private readonly ISubject<Unit> logAllGameObjects = new Subject<Unit>();
    public IObservable<Unit> LogAllGameObjects => logAllGameObjects.AsObservable();

    public IReactiveProperty<string> AliveStateSourceName { get; init; }
        = new ReactiveProperty<string>(string.Empty, ReactivePropertyMode.DistinctUntilChanged);
    public IReactiveProperty<string> DeadStateSourceName { get; init; }
        = new ReactiveProperty<string>(string.Empty, ReactivePropertyMode.DistinctUntilChanged);

    private readonly ISubject<Unit> testAlive = new Subject<Unit>();
    public IObservable<Unit> TestAlive => testAlive.AsObservable();
    private readonly ISubject<Unit> testDead = new Subject<Unit>();
    public IObservable<Unit> TestDead => testDead.AsObservable();

    public IReactiveProperty<bool> PrintLogsToChat { get; init; }
        = new ReactiveProperty<bool>(mode: ReactivePropertyMode.DistinctUntilChanged);
    public IReactiveProperty<int> MinimumVisibleLogLevel { get; init; }
        = new ReactiveProperty<int>(mode: ReactivePropertyMode.DistinctUntilChanged);

    private string[]? inputDevices;
    private string[]? outputDevices;

    private readonly WindowSystem windowSystem;
    private readonly AudioDeviceController audioDeviceController;
    private readonly VoiceRoomManager voiceRoomManager;

    public MainWindow(
        WindowSystem windowSystem,
        AudioDeviceController audioDeviceController,
        VoiceRoomManager voiceRoomManager) : base(
        PluginInitializer.Name)
    {
        this.windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
        this.audioDeviceController = audioDeviceController ?? throw new ArgumentNullException(nameof(audioDeviceController));
        this.voiceRoomManager = voiceRoomManager ?? throw new ArgumentNullException(nameof(voiceRoomManager));
        windowSystem.AddWindow(this);
    }

    public override void Draw()
    {
        if (!Visible)
        {
            return;
        }

        var height = 400;
        ImGui.SetNextWindowSize(new Vector2(400, height), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(400, height), new Vector2(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("ProximityVoiceChat", ref this.visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
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

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            //var rightPad = 110;

            ImGui.AlignTextToFramePadding();
            ImGui.Text("Input Device"); ImGui.TableNextColumn();
            //ImGui.SetNextItemWidth(ImGui.GetColumnWidth() - rightPad);
            this.inputDevices ??= this.audioDeviceController.GetAudioRecordingDevices().ToArray();
            var inputDeviceIndex = this.SelectedAudioInputDeviceIndex.Value + 1;
            if (ImGui.Combo("##InputDevice", ref inputDeviceIndex, this.inputDevices, this.inputDevices.Length))
            {
                this.SelectedAudioInputDeviceIndex.Value = inputDeviceIndex - 1;
            }

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Output Device"); ImGui.TableNextColumn();
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

        //if (ImGui.Button("Log all GameObjects"))
        //{
        //    this.logAllGameObjects.OnNext(Unit.Default);
        //}

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

        foreach(var playerName in this.voiceRoomManager.PlayersInVoiceRoom)
        {
            ImGui.Text(playerName);
        }

        //// Connectivity indicator
        //var drawList = ImGui.GetWindowDrawList();
        //var pos = ImGui.GetCursorScreenPos();
        //var h = ImGui.GetTextLineHeightWithSpacing() / 2f;
        //pos += new Vector2(ImGui.GetWindowSize().X - 110, -h);
        //var radius = 0.6f * h;
        //var color = this.ObsConnected ? Vector4Colors.Green : Vector4Colors.Red;
        //drawList.AddCircleFilled(pos, radius, ImGui.ColorConvertFloat4ToU32(color));
        //pos += new Vector2(radius + 3, -h);
        //ImGui.SetCursorScreenPos(pos);
        //ImGui.Text(this.ObsConnected ? "Connected" : "Not connected");

        //var obsConnectOnStartup = this.ObsConnectOnStartup.Value;
        //if (ImGui.Checkbox("Connect on startup", ref obsConnectOnStartup))
        //{
        //    this.ObsConnectOnStartup.Value = obsConnectOnStartup;
        //}
        //if (ImGui.Button(!this.ObsConnected ? "Connect" : "Disconnect"))
        //{
        //    this.playTestVoice.OnNext(Unit.Default);
        //}
        //if (this.ShowObsAuthError)
        //{
        //    ImGui.SameLine();
        //    ImGui.TextColored(Vector4Colors.Red, "Authentication failed.");
        //}

        ImGui.Indent(-indent);
        ImGui.Spacing();

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
