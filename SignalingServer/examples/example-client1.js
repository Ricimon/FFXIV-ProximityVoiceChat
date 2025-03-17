/*eslint no-constant-condition: ["error", { "checkLoops": false }]*/
import util from "util";
import fs from "fs";
import { setTimeout } from "timers/promises";
import { Buffer } from "node:buffer";
import SignalingChannel from "./signaling-channel.js";
import WebrtcManager from "./webrtc-manager.js";
import dataChannelHandler from "./webrtc-handlers/data-channel-handler.js";

const PORT = process.env.PORT || 3030;
const TOKEN = process.env.TOKEN || "SIGNALING123";
// const SIGNALING_SERVER_URL = "http://localhost:" + PORT;
const SIGNALING_SERVER_URL = "https://ffxivdev.ricimon.com";
const PEER_ID = "testPeer1";
const PEER_TYPE = "admin";
const verbose = true;

const webrtcOptions = {
  enableDataChannel: true,
  enableStreams: false,
  dataChannelHandler,
  verbose,
};

const channel = new SignalingChannel(
  PEER_ID,
  PEER_TYPE,
  SIGNALING_SERVER_URL,
  TOKEN,
  "testPeer1",
  verbose,
);
const manager = new WebrtcManager(
  PEER_ID,
  PEER_TYPE,
  channel,
  webrtcOptions,
  verbose,
);
channel.connect();

var binaryData = fs.readFileSync("examples/sample-3s.wav");
var chunkSize = 1920;
var chunkTime = 20;
(async () => {
  while (true) {
    console.log(
      `Restarting read of audio file - full data length: ${binaryData.length}`,
    );
    var start = 44; // discard the wav header
    while (start < binaryData.length) {
      var toSend = binaryData.slice(start, start + chunkSize);
      var sampleLength = toSend.length;
      var header = Buffer.alloc(2);
      header.writeInt16BE(sampleLength, 0);
      toSend = Buffer.concat([header, toSend]);
      start = start + chunkSize;
      for (const [, peer] of Object.entries(manager.peers)) {
        if (peer.dataChannel.open) {
          peer.dataChannel.send(toSend);
        }
      }
      // console.log(toSend);
      // console.log(
      //   `(${toSend.length})[${[...Uint8Array.from(toSend.slice(0, 20))]},...]`,
      // );
      await setTimeout(chunkTime);
    }
  }
})();

(async () => {
  while (true) {
    console.log(`Peer count: ${Object.keys(manager.peers).length}`);
    for (const [peerId, peer] of Object.entries(manager.peers)) {
      console.log(`(${peerId}) ${util.inspect(peer.dataChannel)}`);
    }
    await setTimeout(1000);
  }
})();
