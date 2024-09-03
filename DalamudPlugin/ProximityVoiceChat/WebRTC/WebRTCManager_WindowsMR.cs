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

public class WebRTCManager_WindowsMR : IDisposable
{
    public IReadOnlyDictionary<string, Peer> Peers => peers;

    private readonly string ourPeerId;
    private readonly string ourPeerType;
    private readonly SignalingChannel signalingChannel;
    private readonly WebRTCOptions options;
    private readonly ILogger logger;
    private readonly bool verbose;
    private readonly PeerConnectionConfiguration config;

    private readonly ConcurrentDictionary<string, Peer> peers = [];
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public WebRTCManager_WindowsMR(string ourPeerId, string ourPeerType, SignalingChannel signalingChannel, WebRTCOptions options, ILogger logger, bool verbose = false)
    {
        this.ourPeerId = ourPeerId;
        this.ourPeerType = ourPeerType;
        this.signalingChannel = signalingChannel;
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
                     Urls =
                     {
                         "stun:stun.l.google.com:19302",
                         "stun:stun1.l.google.com:19302",
                         "stun:stun2.l.google.com:19302"
                     }
                 }
            ]
        };
    }

    public void Dispose()
    {
        if (signalingChannel != null)
        {
            signalingChannel.OnMessage -= OnMessage;
            signalingChannel.OnDisconnected -= OnDisconnected;
        }
        OnDisconnected();
    }

    private void OnMessage(SocketIOResponse response)
    {
        Task.Run(async delegate
        {
            await semaphore.WaitAsync();
            try
            {
                var message = response.GetValue<SignalMessage>();
                var payload = message.payload;
                switch (payload.action)
                {
                    case "open":
                        foreach (var c in payload.connections)
                        {
                            await AddPeer(c.peerId, c.peerType, payload.bePolite);
                        }
                        break;
                    case "close":
                        RemovePeer(peers[message.from]);
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
            catch (Exception e)
            {
                this.logger.Error(e.ToString());
            }
            finally
            {
                semaphore.Release();
            }
        }).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
    }

    private void OnDisconnected()
    {
        foreach (var peerId in peers.Keys.ToList())
        {
            RemovePeer(peers[peerId]);
        }
    }

    private async Task AddPeer(string peerId, string peerType, bool polite, bool canTrickleIceCandidates = true)
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
                //RTCDataChannel = null!,
                MakingOffer = false,
                IgnoreOffer = false,
                IsSettingRemoteAnswerPending = false,
                CanTrickleIceCandidates = canTrickleIceCandidates,
            });
            logger.Debug("Added {0} as a peer.", peerId);
            await peerConnection.InitializeAsync(this.config);
            // Create a data channel if needed
            if (options.EnableDataChannel)
            {
                await peerConnection.AddDataChannelAsync(0, $"{peerId}Channel", true, false);
                //    peers[peerId].RTCDataChannel = await peers[peerId].RTCPeerConnection.createDataChannel($"{peerId}Channel", new RTCDataChannelInit
                //    {
                //        negotiated = true, // the application assumes that data channels are created manually on both peers
                //        id = 0, // data channels created with the same id are connected to each other across peers
                //    });
                try
                {
                    var handler = options.DataChannelHandlerFactory!.CreateHandler();
                    handler.RegisterDataChannel(ourPeerId, ourPeerType, peers[peerId]);
                    peers[peerId].DataChannelHandler = handler;
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                }
            }
            // Update the negotiation logic of the peer
            this.UpdateNegotiationLogic(this.peers[peerId]);
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

        //peerConnection.onicecandidate += candidate => signalingChannel.SendToAsync(peer.PeerId, new SignalMessage.SignalPayload
        //{
        //    action = "ice",
        //    ice = new SignalMessage.SignalPayload.IcePayload
        //    {
        //        candidate = $"candidate:{candidate.candidate}",
        //        sdpMid = candidate.sdpMid ?? "0",
        //        sdpMLineIndex = candidate.sdpMLineIndex,
        //        foundation = candidate.foundation,
        //        component = candidate.component,
        //        priority = candidate.priority,
        //        address = candidate.address,
        //        protocol = candidate.protocol,
        //        port = candidate.port,
        //        type = candidate.type,
        //        tcpType = candidate.tcpType,
        //        relatedAddress = candidate.relatedAddress,
        //        relatedPort = candidate.relatedPort,
        //        usernameFragment = candidate.usernameFragment,
        //    },
        //}).SafeFireAndForget(ex => logger.Error(ex.ToString()));

        //peerConnection.onnegotiationneeded += () => Task.Run(async delegate
        //{
        //    try
        //    {
        //        // impolite peers is always the one who gives an offer
        //        if (!peer.Polite)
        //        {
        //            peer.MakingOffer = true;
        //            var offer = peerConnection.createOffer();
        //            await peerConnection.setLocalDescription(offer);
        //            if (!peer.CanTrickleIceCandidates)
        //            {
        //                await WaitForIceGathering(peer.PeerId, peerConnection);
        //            }
        //            if (verbose)
        //            {
        //                logger.Debug("Sending offer to {0}", peer.PeerId);
        //            }
        //            await signalingChannel.SendToAsync(peer.PeerId, new SignalMessage.SignalPayload
        //            {
        //                action = "sdp",
        //                sdp = new RTCSessionDescriptionInit
        //                {
        //                    type = peerConnection.localDescription.type,
        //                    sdp = peerConnection.localDescription.sdp.ToString(),
        //                },
        //            });
        //        }
        //    }
        //    catch (Exception e)
        //    {
        //        logger.Error(e.ToString());
        //    }
        //    finally
        //    {
        //        peer.MakingOffer = false;
        //    }
        //}).SafeFireAndForget(ex => logger.Error(ex.ToString()));
        //peerConnection.oniceconnectionstatechange += _ =>
        //{
        //    if (peerConnection.iceConnectionState == RTCIceConnectionState.failed)
        //    {
        //        peerConnection.restartIce();
        //    }
        //};
    }

    private Task WaitForIceGathering(string peerId, RTCPeerConnection peerConnection)
    {
        logger.Debug("{0} has trickling disabled. Will gather ALL ICE candidates before sending offer.", peerId);
        var tcs = new TaskCompletionSource();
        if (peerConnection.iceGatheringState == RTCIceGatheringState.complete)
        {
            logger.Debug("Gathering complete");
            tcs.TrySetResult();
        }
        else
        {
            void OnIceGatheringStateChange(RTCIceGatheringState state)
            {
                if (peerConnection.iceGatheringState == RTCIceGatheringState.complete)
                {
                    logger.Debug("Gathering complete");
                    peerConnection.onicegatheringstatechange -= OnIceGatheringStateChange;
                    tcs.TrySetResult();
                }
                else
                {
                    logger.Debug("... gathering ICE candidates...");
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
            //// if we received an offer, check if there is an offer collision (ie. we already have created a local offer and tried to send it)
            //var offerCollision = description.type == RTCSdpType.offer && (peer.MakingOffer || peerConnection.signalingState != RTCSignalingState.stable);
            //peer.IgnoreOffer = !peer.Polite && offerCollision;

            //// Ignore the peer offer if we are impolite and there is an offer collision
            //if (peer.IgnoreOffer)
            //{
            //    if (verbose)
            //    {
            //        logger.Debug("Peer offer was ignored because we are impolite");
            //    }
            //    return;
            //}

            //// Roll back logic for a polite peer that happens to have an offer collision
            //// As of now, this logic doesn't function correctly. TO BE IMPROVED
            //if (offerCollision)
            //{
            //    // If there is a collision we need to rollback
            //    await peerConnection.setLocalDescription(new RTCSessionDescriptionInit { type = RTCSdpType.rollback }); // not working
            //    peerConnection.setRemoteDescription(description); // not working
            //}
            //else
            //{
            //    // Otherwise there are no collision and we can take the offer as our remote description
            //    peerConnection.setRemoteDescription(description);
            //}

            //// When given an offer that we were able to accept, it is time to send back an answer
            //if (description.type == RTCSdpType.offer)
            //{
            //    // create answer and send it
            //    await peerConnection.setLocalDescription(peerConnection.createAnswer());
            //    if (verbose)
            //    {
            //        logger.Debug("Sending answer to {0}", peer.PeerId);
            //    }
            //    await signalingChannel.SendToAsync(peer.PeerId, new SignalMessage.SignalPayload
            //    {
            //        action = "sdp",
            //        sdp = new RTCSessionDescriptionInit
            //        {
            //            type = peerConnection.localDescription.type,
            //            sdp = peerConnection.localDescription.sdp.ToString(),
            //        },
            //    });
            //}
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
            //    // Only add non null candidate (final candidate is null), this version of wrtc requires non null object, future version will handle null candidates
            //    if (candidate != null)
            //    {
            //        peerConnection.addIceCandidate(new RTCIceCandidateInit
            //        {
            //            candidate = candidate.candidate,
            //            sdpMid = candidate.sdpMid,
            //            sdpMLineIndex = candidate.sdpMLineIndex,
            //            usernameFragment = candidate.usernameFragment,
            //        });
            //    }
        }
        catch (Exception e)
        {
            if (!peer.IgnoreOffer)
            {
                logger.Error(e.ToString());
            }
        }
    }

    private void RemovePeer(Peer peer)
    {
        if (peer != null)
        {
            //peer.RTCDataChannel?.close();
            peer.DataChannelHandler?.Dispose();
            peer.PeerConnection?.Dispose();
            peers.TryRemove(peer.PeerId, out _);
            if (verbose)
            {
                logger.Debug("Connection with {0} has been removed", peer.PeerId);
            }
        }
    }
}
