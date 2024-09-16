using AsyncAwaitBestPractices;
using Microsoft.MixedReality.WebRTC;
using ProximityVoiceChat.Log;
using SIPSorcery.Net;
using SocketIOClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProximityVoiceChat.WebRTC;

public class WebRTCManager : IDisposable
{
    public IReadOnlyDictionary<string, Peer> Peers => peers;

    private CancellationTokenSource? disconnectCts;

    private readonly string ourPeerId;
    private readonly string ourPeerType;
    private readonly SignalingChannel signalingChannel;
    private readonly WebRTCOptions options;
    private readonly ILogger logger;
    private readonly bool verbose;
    private readonly PeerConnectionConfiguration config;

    private readonly ConcurrentDictionary<string, Peer> peers = [];
    private readonly SemaphoreSlim onMessageSemaphore = new(1, 1);

    public WebRTCManager(string ourPeerId, string ourPeerType, SignalingChannel signalingChannel, WebRTCOptions options, ILogger logger, bool verbose = false)
    {
        this.ourPeerId = ourPeerId;
        this.ourPeerType = ourPeerType;
        this.signalingChannel = signalingChannel;
        this.signalingChannel.OnConnected += OnConnected;
        this.signalingChannel.OnMessage += OnMessage;
        this.signalingChannel.OnDisconnected += OnDisconnected;
        this.options = options;
        this.logger = logger;
        this.verbose = verbose;
        config = new PeerConnectionConfiguration
        {
            IceServers =
            [
                 new() 
                 {
                     Urls = { options.StunServerUrl },
                 },
                 new()
                 {
                     Urls = { options.TurnServerUrl },
                     TurnUserName = options.TurnUsername,
                     TurnPassword = options.TurnPassword,
                 },
            ]
        };
    }

    public void Dispose()
    {
        if (this.signalingChannel != null)
        {
            this.signalingChannel.OnConnected -= OnConnected;
            this.signalingChannel.OnMessage -= OnMessage;
            this.signalingChannel.OnDisconnected -= OnDisconnected;
        }
        OnDisconnected();
        GC.SuppressFinalize(this);
    }

    private void OnConnected()
    {
        this.disconnectCts?.Dispose();
        this.disconnectCts = new CancellationTokenSource();
    }

    private void OnMessage(SocketIOResponse response)
    {
        // Add peer objects immediately to reflect in UI
        SignalMessage message;
        SignalMessage.SignalPayload payload;
        try
        {
            message = response.GetValue<SignalMessage>();
            payload = message.payload;
            if (payload.action == "open")
            {
                foreach (var c in payload.connections)
                {
                    AddPeer(c.peerId, c.peerType, payload.bePolite);
                }
            }
        }
        catch (Exception e)
        {
            this.logger.Error(e.ToString());
            return;
        }

        // Then run any procedures
        Task.Run(async delegate
        {
            await onMessageSemaphore.WaitAsync();
            try
            {
                switch (payload.action)
                {
                    case "open":
                        foreach (var c in payload.connections)
                        {
                            await InitializePeer(c.peerId, cancellationToken: this.disconnectCts!.Token);
                        }
                        break;
                    case "close":
                        TryRemovePeer(message.from);
                        break;
                    case "sdp":
                        if (verbose)
                        {
                            logger.Debug("Received {0} from {1}", payload.sdp.type, message.from);
                        }
                        await UpdateSessionDescription(peers[message.from], payload.sdp);
                        break;
                    case "ice":
                        UpdateIceCandidate(peers[message.from], payload.ice);
                        break;
                    default:
                        if (verbose)
                        {
                            logger.Debug("Received an unknown action {0}", payload.action);
                        }
                        break;
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception e)
            {
                this.logger.Error(e.ToString());
            }
            finally
            {
                onMessageSemaphore.Release();
            }
        }).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
    }

    private void OnDisconnected()
    {
        this.disconnectCts?.Cancel();
        foreach (var peerId in peers.Keys.ToList())
        {
            TryRemovePeer(peerId);
        }
    }

    private void AddPeer(string peerId, string peerType, bool polite, bool canTrickleIceCandidates = true)
    {
        if (peers.ContainsKey(peerId))
        {
            if (verbose)
            {
                logger.Warn("A peer connection with {0} already exists.", peerId);
            }
        }
        else
        {
            // Add peer to the object of peers
            var peerConnection = new PeerConnection();
            peers.TryAdd(peerId, new Peer
            {
                PeerId = peerId,
                PeerType = peerType,
                Polite = polite,
                PeerConnection = peerConnection,
                MakingOffer = false,
                IgnoreOffer = false,
                IsSettingRemoteAnswerPending = false,
                CanTrickleIceCandidates = canTrickleIceCandidates,
            });
            this.logger.Debug("Added {0} as a peer.", peerId);
        }
    }

    private async Task InitializePeer(string peerId, CancellationToken cancellationToken = default)
    {
        if (peers.TryGetValue(peerId, out var peer))
        {
            this.logger.Debug("Initializing peer connection to {0}", peerId);
            var peerConnection = peer.PeerConnection;
            await peerConnection.InitializeAsync(this.config, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                TryRemovePeer(peerId);
                return;
            }

            // Create a data channel if needed
            if (options.EnableDataChannel)
            {
                this.logger.Debug("Creating data channel for peer {0}", peerId);
                await peerConnection.AddDataChannelAsync(0, $"{peerId}Channel", true, false, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    TryRemovePeer(peerId);
                    return;
                }

                try
                {
                    var handler = options.DataChannelHandlerFactory!.CreateHandler();
                    handler.RegisterDataChannel(ourPeerId, ourPeerType, peer);
                    peer.DataChannelHandler = handler;
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                }
            }
            // Update the negotiation logic of the peer
            this.UpdateNegotiationLogic(peer);
        }
        else
        {
            this.logger.Error("Could not find peer {0} to initialize", peerId);
        }
    }

    private void UpdateNegotiationLogic(Peer peer)
    {
        var peerConnection = peer.PeerConnection;

        peerConnection.Connected += () =>
        {
            this.logger.Debug("PeerConnection: connected.");
        };

        peerConnection.IceStateChanged += (IceConnectionState newState) =>
        {
            this.logger.Debug("ICE state: {0}", newState);
            // TODO: An ICE state of closed should be marked as Connection Error in the UI
        };

        peerConnection.IceCandidateReadytoSend += (IceCandidate candidate) =>
        {
            this.logger.Debug("Ice candidate sending: {0}, {1}, {2}", candidate.SdpMid, candidate.SdpMlineIndex, candidate.Content);
            this.signalingChannel.SendToAsync(peer.PeerId, new SignalMessage.SignalPayload
            {
                action = "ice",
                ice = new SignalMessage.SignalPayload.IcePayload
                {
                    sdpMid = candidate.SdpMid,
                    sdpMLineIndex = candidate.SdpMlineIndex,
                    candidate = candidate.Content,
                }
            }).SafeFireAndForget(ex => logger.Error(ex.ToString()));
        };

        peerConnection.LocalSdpReadytoSend += (SdpMessage message) =>
        {
            Task.Run(async delegate
            {
                try
                {
                    if (verbose)
                    {
                        logger.Debug("Sending Sdp {0} to {1}. Content: {2}", message.Type, peer.PeerId, message.Content);
                    }
                    await signalingChannel.SendToAsync(peer.PeerId, new SignalMessage.SignalPayload
                    {
                        action = "sdp",
                        sdp = new RTCSessionDescriptionInit
                        {
                            type = message.Type == SdpMessageType.Offer ? RTCSdpType.offer : RTCSdpType.answer,
                            sdp = message.Content,
                        },
                    });
                }
                catch(Exception e)
                {
                    logger.Error(e.ToString());
                }
                finally
                {
                    peer.MakingOffer = false;
                }
            }).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
        };

        // impolite peers is always the one who gives an offer
        if (!peer.Polite)
        {
            peer.MakingOffer = true;
            peerConnection.CreateOffer();
        }
    }

    private async Task UpdateSessionDescription(Peer peer, RTCSessionDescriptionInit description)
    {
        try
        {
            var peerConnection = peer.PeerConnection;
            await peerConnection.SetRemoteDescriptionAsync(new SdpMessage
            {
                Type = description.type == RTCSdpType.offer ? SdpMessageType.Offer : SdpMessageType.Answer,
                Content = description.sdp,
            });
            if (description.type == RTCSdpType.offer)
            {
                peerConnection.CreateAnswer();
            }
        }
        catch (Exception e)
        {
            logger.Error(e.ToString());
        }
    }

    private void UpdateIceCandidate(Peer peer, SignalMessage.SignalPayload.IcePayload candidate)
    {
        var peerConnection = peer.PeerConnection;
        try
        {
            if (candidate != null)
            {
                peerConnection.AddIceCandidate(new IceCandidate
                {
                    SdpMid = candidate.sdpMid,
                    SdpMlineIndex = candidate.sdpMLineIndex,
                    Content = candidate.candidate,
                });
            }
        }
        catch (Exception e)
        {
            if (!peer.IgnoreOffer)
            {
                logger.Error(e.ToString());
            }
        }
    }

    private bool TryRemovePeer(string peerId)
    {
        if (peers.TryRemove(peerId, out var peer))
        {
            peer.DataChannelHandler?.Dispose();
            try
            {
                peer.PeerConnection?.Dispose();
            }
            catch (AggregateException e)
            {
                if (e.InnerException is not TaskCanceledException)
                {
                    throw;
                }
            }
            if (verbose)
            {
                logger.Debug("Connection with {0} has been removed", peer.PeerId);
            }
            return true;
        }
        return false;
    }
}
