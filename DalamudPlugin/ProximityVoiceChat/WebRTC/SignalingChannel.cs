using AsyncAwaitBestPractices;
using ProximityVoiceChat.Log;
using SocketIO.Serializer.SystemTextJson;
using SocketIOClient;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ProximityVoiceChat.WebRTC;

public class SignalingChannel : IDisposable
{
    public bool Connected => this.socket.Connected;

    public event Action? OnConnected;
    public event Action<SocketIOResponse>? OnMessage;
    public event Action? OnDisconnected;

    private readonly string peerId;
    private readonly string peerType;
    private readonly SocketIOClient.SocketIO socket;
    private readonly ILogger logger;
    private readonly bool verbose;

    public SignalingChannel(string peerId, string peerType, string signalingServerUrl, string token, ILogger logger, bool verbose = false)
    {
        this.peerId = peerId;
        this.peerType = peerType;
        this.logger = logger;
        this.verbose = verbose;
        this.socket = new SocketIOClient.SocketIO(signalingServerUrl, new SocketIOOptions
        {
            Auth = new Dictionary<string, string>() { { "token", token } },
            Reconnection = false,
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

    public Task ConnectAsync()
    {
        return this.socket.ConnectAsync();
    }

    public Task SendAsync(SignalMessage.SignalPayload payload)
    {
        return this.socket.EmitAsync("message", new SignalMessage
        {
            from = this.peerId,
            target = "all",
            payload = payload,
        });
    }

    public Task SendToAsync(string targetPeerId, SignalMessage.SignalPayload payload)
    {
        return this.socket.EmitAsync("messageOne", new SignalMessage
        {
            from =  this.peerId,
            target = targetPeerId,
            payload = payload,
        });
    }

    public Task DisconnectAsync()
    {
        return this.socket.DisconnectAsync();
    }

    public void Dispose()
    {
        this.OnConnected = null;
        this.OnMessage = null;
        this.OnDisconnected = null;
        this.RemoveListeners();
        this.socket?.Dispose();
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
            this.socket.On("uniquenessError", this.OnUniquenessError);
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
            this.socket.Off("uniquenessError");
        }
    }

    private void OnConnect(object? sender, EventArgs args)
    {
        try
        {
            if (this.verbose)
            {
                this.logger.Debug("Connected to signaling server.");
            }
            this.OnConnected?.Invoke();
            this.socket.EmitAsync("ready", this.peerId, this.peerType).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
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
        if (this.verbose)
        {
            this.logger.Trace("Signaling server message: {0}", response);
        }
        this.OnMessage?.Invoke(response);
    }

    private void OnUniquenessError(SocketIOResponse response)
    {
        this.logger.Error("Uniqueness ERROR: {0}", response);
    }
}
