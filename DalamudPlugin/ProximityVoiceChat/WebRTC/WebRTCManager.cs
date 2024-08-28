using AsyncAwaitBestPractices;
using ProximityVoiceChat.Log;
using SIPSorcery.Net;
using SocketIOClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProximityVoiceChat.WebRTC;

public class WebRTCManager : IDisposable
{
    public IReadOnlyDictionary<string, Peer> Peers => this.peers;

    private readonly string ourPeerId;
    private readonly string ourPeerType;
    private readonly SignalingChannel signalingChannel;
    private readonly WebRTCOptions options;
    private readonly ILogger logger;
    private readonly bool verbose;
    private readonly RTCConfiguration config;

    private readonly ConcurrentDictionary<string, Peer> peers = [];
    private readonly object onMessageLock = new();

    public WebRTCManager(string ourPeerId, string ourPeerType, SignalingChannel signalingChannel, WebRTCOptions options, ILogger logger, bool verbose = false)
    {
        this.ourPeerId = ourPeerId;
        this.ourPeerType = ourPeerType;
        this.signalingChannel = signalingChannel;
        this.signalingChannel.OnMessage += this.OnMessage;
        this.signalingChannel.OnDisconnected += this.OnDisconnected;
        this.options = options;
        this.logger = logger;
        this.verbose = verbose;
        this.config = new RTCConfiguration
        {
            iceServers = [
                new() { urls = "stun:stun.l.google.com:19302" },
                new() { urls = "stun:stun1.l.google.com:19302" },
                new() { urls = "stun:stun2.l.google.com:19302" }]
        };
    }

    public void Dispose()
    {
        if (signalingChannel != null)
        {
            this.signalingChannel.OnMessage -= this.OnMessage;
            this.signalingChannel.OnDisconnected -= this.OnDisconnected;
        }
        OnDisconnected();
    }
    
    private void OnMessage(SocketIOResponse response)
    {
        try
        {
            lock (onMessageLock)
            {
                var message = response.GetValue<SignalMessage>();
                var payload = message.payload;
                switch (payload.action)
                {
                    case "open":
                        foreach (var c in payload.connections)
                        {
                            this.AddPeer(c.peerId, c.peerType, payload.bePolite).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
                        }
                        break;
                    case "close":
                        this.RemovePeer(this.peers[message.from]);
                        break;
                    case "sdp":
                        if (this.verbose)
                        {
                            this.logger.Debug("Received {0} from {1}", payload.sdp.type, message.from);
                        }
                        this.UpdateSessionDescription(this.peers[message.from], payload.sdp).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
                        break;
                    case "ice":
                        this.UpdateIceCandidate(this.peers[message.from], payload.ice);
                        break;
                    default:
                        if (this.verbose)
                        {
                            this.logger.Debug("Received an unknown action {0}", payload.action);
                        }
                        break;
                }
            }
        }
        catch (Exception e)
        {
            this.logger.Error(e.ToString());
        }
    }

    private void OnDisconnected()
    {
        foreach(var peerId in this.peers.Keys.ToList())
        {
            RemovePeer(this.peers[peerId]);
        }
    }

    private async Task AddPeer(string peerId, string peerType, bool polite, bool canTrickleIceCandidates = true)
    {
        if (this.peers.ContainsKey(peerId))
        {
            if (this.verbose)
            {
                this.logger.Warn("A peer connection with {0} already exists.", peerId);
            }
        }
        else
        {
            // Add peer to the object of peers
            this.peers.TryAdd(peerId, new Peer
            {
                PeerId = peerId,
                PeerType = peerType,
                Polite = polite,
                RTCPeerConnection = new RTCPeerConnection(this.config),
                RTCDataChannel = null!,
                MakingOffer = false,
                IgnoreOffer = false,
                IsSettingRemoteAnswerPending = false,
                CanTrickleIceCandidates = canTrickleIceCandidates,
            });
            this.logger.Debug("Added {0} as a peer.", peerId);
            // Create a data channel if needed
            if (this.options.EnableDataChannel)
            {
                this.peers[peerId].RTCDataChannel = await this.peers[peerId].RTCPeerConnection.createDataChannel($"{peerId}Channel", new RTCDataChannelInit
                {
                    negotiated = true, // the application assumes that data channels are created manually on both peers
                    id = 0, // data channels created with the same id are connected to each other across peers
                });
                try
                {
                    this.options.DataChannelHandler?.RegisterDataChannel(this.ourPeerId, this.ourPeerType, this.peers[peerId]);
                }
                catch (Exception e)
                {
                    this.logger.Error(e.ToString());
                }
            }
            // Update the negotiation logic of the peer
            this.UpdateNegotiationLogic(this.peers[peerId]);
        }
    }

    private void UpdateNegotiationLogic(Peer peer)
    {
        var peerConnection = peer.RTCPeerConnection;
        peerConnection.onicecandidate += candidate => this.signalingChannel.SendToAsync(peer.PeerId, new SignalMessage.SignalPayload
        {
            action = "ice",
            ice = new SignalMessage.SignalPayload.IcePayload
            {
                candidate = $"candidate:{candidate.candidate}",
                sdpMid = candidate.sdpMid ?? "0",
                sdpMLineIndex = candidate.sdpMLineIndex,
                foundation = candidate.foundation,
                component = candidate.component,
                priority = candidate.priority,
                address = candidate.address,
                protocol = candidate.protocol,
                port = candidate.port,
                type = candidate.type,
                tcpType = candidate.tcpType,
                relatedAddress = candidate.relatedAddress,
                relatedPort = candidate.relatedPort,
                usernameFragment = candidate.usernameFragment,
            },
        }).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));

        peerConnection.onnegotiationneeded += async () =>
        {
            try
            {
                // impolite peers is always the one who gives an offer
                if (!peer.Polite)
                {
                    peer.MakingOffer = true;
                    var offer = peerConnection.createOffer();
                    await peerConnection.setLocalDescription(offer);
                    if (!peer.CanTrickleIceCandidates)
                    {
                        await this.WaitForIceGathering(peer.PeerId, peerConnection);
                    }
                    if (this.verbose)
                    {
                        this.logger.Debug("Sending offer to {0}", peer.PeerId);
                    }
                    await this.signalingChannel.SendToAsync(peer.PeerId, new SignalMessage.SignalPayload
                    {
                        action = "sdp",
                        sdp = new RTCSessionDescriptionInit
                        {
                            type = peerConnection.localDescription.type,
                            sdp = peerConnection.localDescription.sdp.ToString(),
                        },
                    });
                }
            }
            catch (Exception e)
            {
                this.logger.Error(e.ToString());
            }
            finally
            {
                peer.MakingOffer = false;
            }
        };
        peerConnection.oniceconnectionstatechange += _ =>
        {
            if (peerConnection.iceConnectionState == RTCIceConnectionState.failed)
            {
                peerConnection.restartIce();
            }
        };
    }

    private Task WaitForIceGathering(string peerId, RTCPeerConnection peerConnection)
    {
        this.logger.Debug("{0} has trickling disabled. Will gather ALL ICE candidates before sending offer.", peerId);
        var tcs = new TaskCompletionSource();
        if (peerConnection.iceGatheringState == RTCIceGatheringState.complete)
        {
            this.logger.Debug("Gathering complete");
            tcs.TrySetResult();
        }
        else
        {
            void OnIceGatheringStateChange(RTCIceGatheringState state)
            {
                if (peerConnection.iceGatheringState == RTCIceGatheringState.complete)
                {
                    this.logger.Debug("Gathering complete");
                    peerConnection.onicegatheringstatechange -= OnIceGatheringStateChange;
                    tcs.TrySetResult();
                }
                else
                {
                    this.logger.Debug("... gathering ICE candidates...");
                }
            }
            peerConnection.onicegatheringstatechange += OnIceGatheringStateChange;
        }
        return tcs.Task;
    }

    private async Task UpdateSessionDescription(Peer peer, RTCSessionDescriptionInit description)
    {
        try
        {
            var peerConnection = peer.RTCPeerConnection;
            // if we received an offer, check if there is an offer collision (ie. we already have created a local offer and tried to send it)
            var offerCollision = description.type == RTCSdpType.offer && (peer.MakingOffer || peerConnection.signalingState != RTCSignalingState.stable);
            peer.IgnoreOffer = !peer.Polite && offerCollision;

            // Ignore the peer offer if we are impolite and there is an offer collision
            if (peer.IgnoreOffer)
            {
                if (this.verbose)
                {
                    this.logger.Debug("Peer offer was ignored because we are impolite");
                }
                return;
            }

            // Roll back logic for a polite peer that happens to have an offer collision
            // As of now, this logic doesn't function correctly. TO BE IMPROVED
            if (offerCollision)
            {
                // If there is a collision we need to rollback
                await peerConnection.setLocalDescription(new RTCSessionDescriptionInit { type = RTCSdpType.rollback }); // not working
                peerConnection.setRemoteDescription(description); // not working
            }
            else
            {
                // Otherwise there are no collision and we can take the offer as our remote description
                peerConnection.setRemoteDescription(description);
            }

            // When given an offer that we were able to accept, it is time to send back an answer
            if (description.type == RTCSdpType.offer)
            {
                // create answer and send it
                await peerConnection.setLocalDescription(peerConnection.createAnswer());
                if (this.verbose)
                {
                    this.logger.Debug("Sending answer to {0}", peer.PeerId);
                }
                await this.signalingChannel.SendToAsync(peer.PeerId, new SignalMessage.SignalPayload
                {
                    action = "sdp",
                    sdp = new RTCSessionDescriptionInit
                    {
                        type = peerConnection.localDescription.type,
                        sdp = peerConnection.localDescription.sdp.ToString(),
                    },
                });
            }
        }
        catch (Exception e)
        {
            this.logger.Error(e.ToString());
        }
    }

    private void UpdateIceCandidate(Peer peer, SignalMessage.SignalPayload.IcePayload candidate)
    {
        var peerConnection = peer.RTCPeerConnection;
        try
        {
            // Only add non null candidate (final candidate is null), this version of wrtc requires non null object, future version will handle null candidates
            if (candidate != null)
            {
                peerConnection.addIceCandidate(new RTCIceCandidateInit
                {
                    candidate = candidate.candidate,
                    sdpMid = candidate.sdpMid,
                    sdpMLineIndex = candidate.sdpMLineIndex,
                    usernameFragment = candidate.usernameFragment,
                });
            }
        }
        catch (Exception e)
        {
            if (!peer.IgnoreOffer)
            {
                this.logger.Error(e.ToString());
            }
        }
    }

    private void RemovePeer(Peer peer)
    {
        if (peer != null)
        {
            peer.RTCPeerConnection?.close();
            peer.RTCDataChannel?.close();
            this.peers.TryRemove(peer.PeerId, out _);
            if (this.verbose)
            {
                this.logger.Debug("Connection with {0} has been removed", peer.PeerId);
            }
        }
    }
}
