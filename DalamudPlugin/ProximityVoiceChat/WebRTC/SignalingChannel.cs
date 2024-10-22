using AsyncAwaitBestPractices;
using ProximityVoiceChat.Log;
using SocketIO.Serializer.SystemTextJson;
using SocketIOClient;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ProximityVoiceChat.WebRTC;

public class SignalingChannel : IDisposable
{
    public bool Connected => !(this.disconnectCts?.IsCancellationRequested ?? false) && this.socket.Connected;
    public bool Ready => this.Connected && this.ready;
    public bool Disconnected { get; private set; }
    public string PeerId { get; }
    public string PeerType { get; }
    public string? RoomName { get; private set; }

    public event Action? OnConnected;
    public event Action? OnReady;
    public event Action<SocketIOResponse>? OnMessage;
    public event Action? OnDisconnected;

    private CancellationTokenSource? disconnectCts;
    private string? roomPassword;
    private bool ready;

    private readonly SocketIOClient.SocketIO socket;
    private readonly ILogger logger;
    private readonly bool verbose;

    public SignalingChannel(string peerId, string peerType, string signalingServerUrl, string token, ILogger logger, bool verbose = false)
    {
        this.PeerId = peerId;
        this.PeerType = peerType;
        this.logger = logger;
        this.verbose = verbose;
        this.socket = new SocketIOClient.SocketIO(signalingServerUrl, new SocketIOOptions
        {
            Auth = new Dictionary<string, string>() { { "token", token } },
            Reconnection = true,
        });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        this.socket.Serializer = new SystemTextJsonSerializer(options);
        this.AddListeners();
    }

    public Task ConnectAsync(string roomName = "", string roomPassword = "")
    {
        if (this.socket.Connected)
        {
            this.logger.Error("Signaling server is already connected.");
            return Task.CompletedTask;
        }
        this.ready = false;
        this.disconnectCts?.Dispose();
        this.disconnectCts = new();
        this.Disconnected = false;
        this.RoomName = roomName;
        this.roomPassword = roomPassword;
        return this.socket.ConnectAsync(this.disconnectCts.Token);
    }

    public Task SendAsync(SignalMessage.SignalPayload payload)
    {
        if (this.socket.Connected)
        {
            return this.socket.EmitAsync("message", new SignalMessage
            {
                from = this.PeerId,
                target = "all",
                payload = payload,
            });
        }
        else
        {
            return Task.CompletedTask;
        }
    }

    public Task SendToAsync(string targetPeerId, SignalMessage.SignalPayload payload)
    {
        if (this.socket.Connected)
        {
            return this.socket.EmitAsync("messageOne", new SignalMessage
            {
                from = this.PeerId,
                target = targetPeerId,
                payload = payload,
            });
        }
        else
        {
            return Task.CompletedTask;
        }
    }

    public Task DisconnectAsync()
    {
        this.Disconnected = true;
        if (this.socket.Connected)
        {
            return this.socket.DisconnectAsync();
        }
        else
        {
            this.logger.Debug("Cancelling signaling server connection.");
            this.disconnectCts?.Cancel();
            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        this.OnConnected = null;
        this.OnReady = null;
        this.OnMessage = null;
        this.OnDisconnected = null;
        this.RemoveListeners();
        this.socket?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void AddListeners()
    {
        if (this.socket != null)
        {
            this.socket.OnConnected += this.OnConnect;
            this.socket.OnDisconnected += this.OnDisconnect;
            this.socket.OnError += this.OnError;
            this.socket.OnReconnected += this.OnReconnect;
            this.socket.On("message", this.OnMessageCallback);
            this.socket.On("serverDisconnect", this.OnServerDisconnect);
        }
    }

    private void RemoveListeners()
    {
        if (this.socket != null)
        {
            this.socket.OnConnected -= this.OnConnect;
            this.socket.OnDisconnected -= this.OnDisconnect;
            this.socket.OnError -= this.OnError;
            this.socket.OnReconnected -= this.OnReconnect;
            this.socket.Off("message");
            this.socket.Off("serverDisconnect");
        }
    }

    private void OnConnect(object? sender, EventArgs args)
    {
        try
        {
            if (!Connected)
            {
                return;
            }

            if (this.verbose)
            {
                this.logger.Debug("Connected to signaling server.");
            }
            this.OnConnected?.Invoke();
            this.socket.EmitAsync("ready", this.PeerId, this.PeerType, this.RoomName, this.roomPassword)
                .SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
        }
        catch (Exception ex)
        {
            this.logger.Error(ex.ToString());
        }
    }

    private void OnDisconnect(object? sender, string reason)
    {
        try
        {
            if (this.verbose)
            {
                this.logger.Debug("Disconnected from signaling server, reason: {0}", reason);
            }
            this.Disconnected = true;
            this.OnDisconnected?.Invoke();
        }
        catch (Exception ex)
        {
            this.logger.Error(ex.ToString());
        }
    }

    private void OnError(object? sender, string error)
    {
        this.logger.Error("Signaling server ERROR: " + error);
        // An errored socket is considered disconnected, but we'll need to manually set disconnection state
        // and cancel the connection attempt.
        this.Disconnected = true;
        this.disconnectCts?.Cancel();
        this.disconnectCts?.Dispose();
        this.disconnectCts = null;
        // There's a known exception here when attempting to connect again, due to the strange way
        // the Socket.IO for .NET library internally handles Task state transitions.
    }

    private void OnReconnect(object? sender, int attempts)
    {
        if (this.verbose)
        {
            this.logger.Info("Signaling server reconnect, attempts: {0}", attempts);
        }
    }

    private void OnMessageCallback(SocketIOResponse response)
    {
        if (!Connected)
        {
            return;
        }

        //if (this.verbose)
        //{
        //    this.logger.Trace("Signaling server message: {0}", response);
        //}
        this.OnMessage?.Invoke(response);
        // Assume that any message callback implies readiness
        if (!this.ready)
        {
            this.ready = true;
            this.OnReady?.Invoke();
        }
    }

    private void OnServerDisconnect(SocketIOResponse response)
    {
        if (!Connected)
        {
            return;
        }

        this.logger.Error("Signaling server disconnect: {0}", response);

        // This message auto disconnects the client, but does not immediately set the socket state to not Connected.
        // So we need to dispose and nullify the token to avoid calling Cancel on the token, which for some reason
        // throws an exception due to cancellation token subscriptions.
        this.disconnectCts?.Dispose();
        this.disconnectCts = null;
    }
}
