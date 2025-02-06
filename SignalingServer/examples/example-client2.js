import SignalingChannel from "./signaling-channel.js";
import WebrtcManager from "./webrtc-manager.js";
import dataChannelHandler from "./webrtc-handlers/data-channel-handler.js";

const PORT = process.env.PORT || 3030;
const TOKEN = process.env.TOKEN || "SIGNALING123";
const SIGNALING_SERVER_URL = "http://localhost:" + PORT;
const PEER_ID = "testPeer2";
const PEER_TYPE = "admin";
const verbose = true;

const webrtcOptions = { enableDataChannel: true, enableStreams: false, dataChannelHandler, verbose };

const channel = new SignalingChannel(PEER_ID, PEER_TYPE, SIGNALING_SERVER_URL, TOKEN, verbose);
const manager = new WebrtcManager(PEER_ID, PEER_TYPE, channel, webrtcOptions, verbose);
channel.connect();
