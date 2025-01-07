// IMPORTS
import http from "http";
import express from "express";
import { Server } from "socket.io";
import cors from "cors";
import sirv from "sirv";
import { JSONFilePreset } from 'lowdb/node';
import { state } from './state.js';

// ENVIRONMENT VARIABLES
const PORT = process.env.PORT || 3030;
const DEV = process.env.NODE_ENV === "development";
const TOKEN = process.env.TOKEN;

// SETUP SERVERS
const app = express();
app.use(express.json(), cors());
const server = http.createServer(app);
const io = new Server(server, { cors: {} });

// SETUP DB
const defaultData = { roomProperties: {} };
const db = await JSONFilePreset('db.json', defaultData);
const { roomProperties } = db.data;
const rooms = state.rooms;
const connections = state.connections;

// AUTHENTICATION MIDDLEWARE
io.use((socket, next) => {
  const token = socket.handshake.auth.token; // check the auth token provided by the client upon connection
  if (token === TOKEN) {
    next();
  } else {
    next(new Error("Authentication error"));
  }
});

// API ENDPOINT TO DISPLAY THE CONNECTION TO THE SIGNALING SERVER
app.get("/connections", (req, res) => {
  res.json(Object.values(connections));
});
app.get("/rooms", (req, res) => {
  res.json(rooms);
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

// MESSAGING LOGIC
io.on("connection", (socket) => {
  console.log("User connected with id", socket.id);

  socket.on("ready", async (peerId, peerType, roomName, roomPassword, playersInInstance) => {
    // Make sure that the hostname is unique, if the hostname is already in connections, send an error and disconnect
    if (peerId in connections) {
      socket.emit("serverDisconnect", {
        message: `${peerId} is already connected to the signaling server. Please change your peer ID and try again.`,
      });
      socket.disconnect(true);
      return;
    }

    // Check room name
    if (!roomName) {
      // for debugging
      if (peerType === "admin") {
        roomName = "public";
      }
      else {
        socket.emit("serverDisconnect", {
          message: "Room name not given, cannot connect.",
        });
        socket.disconnect(true);
        return;
      }
    }

    let instanceNumber = 0;

    if (roomName.startsWith("public")) {
      // Public room
      instanceNumber = 1;
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
          socket.emit("serverDisconnect", {
            message: `Cannot connect to room ${roomName}, room does not exist.`,
          });
          socket.disconnect(true);
          return;
        }
        if (roomPassword !== roomProperties[roomName].password) {
          socket.emit("serverDisconnect", {
            message: `Cannot connect to room ${roomName}, incorrect password.`,
          });
          socket.disconnect(true);
          return;
        }
      }
    }

    let socketRoomName = getSocketRoomName(roomName, instanceNumber);

    // Join socket room
    socket.join(socketRoomName);
    socket.room = socketRoomName;
    console.log(`Added ${peerId} to connections, in room ${socketRoomName}`);

    // Get (or create) room and instance
    let room = rooms[roomName] || (rooms[roomName] = {});
    let instance = room[instanceNumber] || (room[instanceNumber] = {});

    // Let new peer know about all existing peers in its instance
    if (!isEmpty(instance)) {
      socket.send({
        from: "all",
        target: peerId,
        payload: { action: "open", connections: Object.values(instance), bePolite: false } // The new peer doesn't need to be polite.
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
      payload: { action: "open", connections: [newPeer], bePolite: true }, // send connections object with an array containing the only new peer and make all existing peers polite.
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
      console.log(`Target ${target} not found`);
    }
  });

  socket.on("disconnect", () => {
    const disconnectingPeer = Object.values(connections).find((peer) => peer.socketId === socket.id);
    if (disconnectingPeer) {
      console.log(`Disconnected ${socket.id} with peerId ${disconnectingPeer.peerId} from room ${socket.room}`);
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
      console.log(socket.id, "has disconnected");
    }
  });
});

// SERVE STATIC FILES
app.use(sirv("public", { DEV }));

// RUN APP
server.listen(PORT, console.log(`Listening on PORT ${PORT}`));
