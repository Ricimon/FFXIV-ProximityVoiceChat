// IMPORTS
import pino from "pino"
import crypto from "crypto";
import http from "http";
import express from "express";
import basicAuth from "express-basic-auth";
import { Server } from "socket.io";
import cors from "cors";
import sirv from "sirv";
import { JSONFilePreset } from "lowdb/node";
import client from "prom-client";
import ioMetrics from "socket.io-prometheus";
import { state } from "./state.js";

// LOGGER
const transport = pino.transport({
  targets: [
    {
      level: "trace",
      target: "pino/file",
      options: {
        destination: `${import.meta.dirname}/../logs/server.log`,
        mkdir: true,
      },
    },
    {
      level: "trace",
      target: "pino/file",
      options: {
        destination: 1,
      },
    },
  ],
});
const logger = pino(transport);

// ENVIRONMENT VARIABLES
const PORT = process.env.PORT || 3030;
const DEV = process.env.NODE_ENV === "development";
const TOKEN = process.env.TOKEN;
const WEB_USER = process.env.WEB_USER || "admin";
const WEB_PASS = process.env.WEB_PASS || "";
const TURN_URL = process.env.TURN_URL || "";
const TURN_SECRET = process.env.TURN_SECRET || "secret";

// SETUP SERVERS
const app = express();
app.use(express.json(), cors(), 
  (req, res, next) => {
    // expect this server to be placed behind a reverse proxy
    if (req.headers["x-forwarded-host"]) {
      basicAuth({
        users: { [WEB_USER] : WEB_PASS },
        challenge: true,
        realm: "FFXIV-ProximityVoiceChat-WebServer",
      })(req, res, next);
    }
    else { next(); }
  });
const server = http.createServer(app);
const io = new Server(server, { cors: {} });

// SETUP DB
const defaultData = { roomProperties: {} };
const db = await JSONFilePreset("db.json", defaultData);
const { roomProperties } = db.data;
const rooms = state.rooms;
const connections = state.connections;

// SETUP PROMETHEUS METRICS
const register = client.register;
client.collectDefaultMetrics({
  app: "FFXIV-ProximityVoiceChat-SignalingServer",
  //prefix: "node_",
  timeout: 10000,
  gcDuration: [0.001, 0.01, 0.1, 1, 2, 5],
  register
});
ioMetrics(io);

// EXPRESS HTTP ENDPOINTS
app.get("/connections", (req, res) => {
  res.json(connections);
});
app.get("/rooms", (req, res) => {
  res.json(rooms);
});
app.get("/metrics", async (req, res) => {
  res.setHeader("Content-Type", register.contentType);
  res.send(await register.metrics());
})

// AUTHENTICATION MIDDLEWARE
io.use((socket, next) => {
  const token = socket.handshake.auth.token; // check the auth token provided by the client upon connection
  if (token === TOKEN) {
    next();
  } else {
    next(new Error("Authentication error"));
  }
});

// UTILITY FUNCTIONS
function isEmpty(obj) {
  for (const prop in obj) {
    if (Object.hasOwn(obj, prop)) {
      return false;
    }
  }
  return true;
}
function getSocketRoomName(roomName, instance) {
  return `${roomName}-${instance}`;
}
//https://stackoverflow.com/a/35767224
function getTURNConfig() {
  var unixTimestamp = Math.floor(Date.now() / 1000) + 24*3600, // this credential would be valid for the next 24 hours
    username = [unixTimestamp, "ffxivproximityvoicechat"].join(':'),
    password,
    hmac = crypto.createHmac("sha1", TURN_SECRET);
  hmac.setEncoding("base64");
  hmac.write(username);
  hmac.end();
  password = hmac.read();
  return {
    url: TURN_URL,
    username: username,
    password: password
  };
}

// MESSAGING LOGIC
io.on("connection", (socket) => {
  logger.info(`(${socket.id}) User connected`);

  socket.on("ready", async (peerId, peerType, roomName, roomPassword, playersInInstance) => {
      logger.info(`(${socket.id}) Player ${peerId} ready event received with peerType ${peerType}, roomName ${roomName}, roomPassword ${roomPassword}, and playersInInstance [${playersInInstance}]`);

    // Make sure that the hostname is unique, if the hostname is already in connections, send an error and disconnect
    if (peerId in connections) {
      const msg = `(${socket.id}) ${peerId} is already connected to the signaling server. Disconnecting.`;
      logger.info(msg);
      socket.emit("serverDisconnect", { message: msg });
      socket.disconnect(true);
      return;
    }

    // Check room name
    if (!roomName) {
      // for debugging
      if (peerType === "admin") {
        roomName = "public_Admin";
      }
      else {
        const msg = `(${socket.id}) Room name not given, cannot connect.`;
        logger.info(msg);
        socket.emit("serverDisconnect", { message: msg });
        socket.disconnect(true);
        return;
      }
    }

    let instanceNumber = 0;

    if (roomName.startsWith("public")) {
      // Public room
      const instanceArg = roomName.split("_", 2)[1];
      if (instanceArg === undefined || instanceArg === "Unknown") {
        const msg = `(${socket.id}) Invalid public room name.`;
        logger.info(msg);
        socket.emit("serverDisconnect", { message: msg });
        socket.disconnect(true);
      }

      if (instanceArg === "Instance") {
        // Try to find a player already in the map, which will correspond to the same instance for the connecting player
        if (playersInInstance) {
          for (const p of playersInInstance) {
            if (p in connections && connections[p].roomName === roomName) {
              const foundPeer = connections[p];
              logger.info(`(${socket.id}) Found player ${p} in existing room instance ${getSocketRoomName(foundPeer.roomName, foundPeer.instanceNumber)}`)
              instanceNumber = connections[p].instanceNumber;
              break;
            }
          }
        }

        // If no existing players found, create a new instance for the map
        if (instanceNumber === 0) {
          instanceNumber = 1;
          const room = rooms[roomName];
          if (room) {
            while (instanceNumber in room) {
              instanceNumber++;
            }
          }
          logger.info(`(${socket.id}) No players found in existing room instances. Creating new instance with number ${instanceNumber}`);
        }
      }
    }
    else {
      // Private room
      if (peerId === roomName) {
        // Room owner sets room password
        roomProperties[roomName] = { password: roomPassword };
        await db.update(({ roomProperties }) => roomProperties[roomName] = { password: roomPassword });
      } else {
        // Check that we can connect to an existing private room
        if (!(roomName in roomProperties)) {
          const msg = `(${socket.id}) Failed to join room ${roomName}, room does not exist.`;
          logger.info(msg);
          socket.emit("serverDisconnect", { message: msg });
          socket.disconnect(true);
          return;
        }
        
        // Check password
        if (roomPassword !== roomProperties[roomName].password) {
          const msg = `(${socket.id}) Failed to join room ${roomName}, incorrect password.`;
          logger.info(msg);
          socket.emit("serverDisconnect", { message: msg });
          socket.disconnect(true);
          return;
        }
      }
    }

    let socketRoomName = getSocketRoomName(roomName, instanceNumber);

    // Join socket room
    socket.join(socketRoomName);
    socket.room = socketRoomName;
    logger.info(`(${socket.id}) Added ${peerId} to connections, in room ${socketRoomName}`);

    // Get (or create) room and instance
    let room = rooms[roomName] || (rooms[roomName] = {});
    let instance = room[instanceNumber] || (room[instanceNumber] = {});

    // Let new peer know about all existing peers in its instance
    if (!isEmpty(instance)) {
      socket.send({
        from: "all",
        target: peerId,
        payload: { 
          action: "open",
          connections: Object.values(instance),
          bePolite: false, // The new peer doesn't need to be polite.
          turnConfig: getTURNConfig()
        },
      });
    }

    // Create new peer
    const newPeer = { 
      socketId: socket.id,
      peerId,
      peerType,
      roomName,
      instanceNumber,
    };
    // Updates connections object
    connections[peerId] = newPeer;
    // Update instance object
    instance[peerId] = newPeer;

    // Let all other peers know about new peer
    socket.to(socket.room).emit("message", {
      from: peerId,
      target: "all",
      payload: { 
        action: "open",
        connections: [newPeer],
        bePolite: true,  // send connections object with an array containing the only new peer and make all existing peers polite.
        turnConfig: getTURNConfig()
      },
    })
  });

  socket.on("message", (message) => {
    if (message.payload && message.payload.action === "update") {
      for (const c of message.payload.connections) {
        // Peer data can only be updated by its owner
        if (c.peerId === message.from && c.peerId in connections) {
          connections[c.peerId].audioState = c.audioState;
        }
      }
    }
    // Send message to all peers except the sender
    socket.to(socket.room).emit("message", message);
  });

  socket.on("messageOne", (message) => {
    // Send message to a specific targeted peer
    const { target } = message;
    const targetPeer = connections[target];
    if (targetPeer) {
      io.to(targetPeer.socketId).emit("message", { ...message });
    } else {
      logger.info(`(${socket.id}) Target ${target} not found`);
    }
  });

  socket.on("disconnect", () => {
    const disconnectingPeer = Object.values(connections).find((peer) => peer.socketId === socket.id);
    if (disconnectingPeer) {
      logger.info(`(${socket.id}) Disconnected peerId ${disconnectingPeer.peerId} from room ${socket.room}`);
      // Make all peers close their peer channels
      socket.to(socket.room).emit("message", {
        from: disconnectingPeer.peerId,
        target: "all",
        payload: { action: "close", message: "Peer has left the signaling server" },
      });
      // remove disconnecting peer from connections
      delete connections[disconnectingPeer.peerId];
      // remove disconnecting peer from instance
      let room = rooms[disconnectingPeer.roomName];
      let instance = room[disconnectingPeer.instanceNumber];
      delete instance[disconnectingPeer.peerId];
      // remove instance if empty
      if (isEmpty(instance)) {
        delete room[disconnectingPeer.instanceNumber];
      }
      // remove room if empty
      if (isEmpty(room)) {
        delete rooms[disconnectingPeer.roomName];
      }
    } else {
      logger.info(`(${socket.id}) User disconnected`);
    }
  });
});

// SERVE STATIC FILES
app.use(sirv("public", { DEV }));

// RUN APP
server.listen(PORT, logger.info(`Listening on PORT ${PORT}`));
