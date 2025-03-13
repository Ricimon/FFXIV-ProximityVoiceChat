import util from "util";
import io from "socket.io-client";

/**
 * Class represeting the signaling channel used to establish WebRTC communication in a meshed network of peers.
 * Connects to the Signaling server and enables messaging to all peers or only one peer.
 *
 * @class SignalingChannel
 */
class SignalingChannel {
    /**
     * Creates an instance of SignalingChannel.
     * @param {String} peerId - Id to identify the peer in the signaling server and the WebRTC communication. Must be unique for each peer, otherwise a Uniqueness error will occur.
     * @param {String} peerType - What type of peer this is, this is up to the application. For example peerType can be 'admin', 'vehicle', 'controltower' or 'robot' depending on your application
     * @param {String} signalingServerUrl - URL to the signaling server
     * @param {String} token - The token used to authenticate the connection to the signaling server
     * @param {String} roomName - The room name to join on the signaling server
     * @param {Boolean} verbose - If true the channel will print its status when events occur
     * @memberof SignalingChannel
     */
    constructor(peerId, peerType, signalingServerUrl, token, roomName, verbose = false) {
        this.peerId = peerId;
        this.peerType = peerType;
        this.roomName = roomName;
        this.verbose = verbose;
        this.resetListeners();
        this.socket = new io(signalingServerUrl, {
            auth: { token },
            autoConnect: false, // disables auto connection, by default the client would connect to the server as soon as the io() object is instatiated
            reconnection: true, // enables auto reconnection to server, this can occur when for example the host server disconnects. When set to true, the client would keep trying to reconnect
            // for a complete list of the available options, see https://socket.io/docs/v4/client-api/#new-Manager-url-options
        });
    }
    /**
     * Updates the eventlisteners and connects the socket to the signaling server.
     *
     * @memberof SignalingChannel
     */
    connect() {
        this.updateListeners();
        this.socket.connect();
    }
    /**
     * Sends a message to all other peers in the channel.
     *
     * @param {Object} payload - The data to send to all peers
     * @memberof SignalingChannel
     */
    send(payload) {
        this.socket.emit("message", { from: this.peerId, target: "all", payload });
    }
    /**
     * Sends a message to a specific peer in the channel, based on their peerId
     *
     * @param {string} targetPeerId - Id of the peer who should recieve the message
     * @param {Object} payload - The data to send to the peer
     * @memberof SignalingChannel
     */
    sendTo(targetPeerId, payload) {
        this.socket.emit("messageOne", { from: this.peerId, target: targetPeerId, payload });
    }
    /**
     * Resets all event listers and disconnects the peer from the signaling server
     *
     * @memberof SignalingChannel
     */
    disconnect() {
        if (this.socket) {
            this.resetListeners();
            this.socket.disconnect();
        }
    }
    /**
     * Assigns new event listeners to the socket. Must be called after resetListeners()
     *
     * @memberof SignalingChannel
     */
    updateListeners() {
        if (this.socket) {
            this.socket.on("connect", this.onConnect);
            this.socket.on("disconnect", this.onDisconnect);
            this.socket.on("connect_error", this.onError);
            this.socket.on("reconnect", this.onReconnect);
            this.socket.on("message", this.onMessage);
            this.socket.on("uniquenessError", this.onUniquenessError);
        }
    }
    /**
     * Unassigns all exisitng event listeners on the socket and defines
     *     the default values for all event callbacks
     *
     * @memberof SignalingChannel
     */
    resetListeners() {
        if (this.socket) {
            this.socket.off("connect", this.onConnect);
            this.socket.off("disconnect", this.onDisconnect);
            this.socket.off("connect_error", this.onError);
            this.socket.off("reconnect", this.onReconnect);
            this.socket.off("message", this.onMessage);
            this.socket.off("uniquenessError", this.onUniquenessError);
        }
        this.onConnect = () => {
            this.verbose ? console.log("Connected to signaling server with id", this.socket.id) : "";
            this.socket.emit("ready", this.peerId, this.peerType, this.roomName, "0000");
        };
        this.onDisconnect = () => (this.verbose ? console.log("Disconnected from signaling server") : "");
        this.onError = (error) => (this.verbose ? console.log("Signaling Server ERROR", error) : "");
        this.onReconnect = (nr) => (this.verbose ? console.log("Reconnected to signaling server, with", nr, "attempts") : "");
        this.onMessage = (message) => (this.verbose ? console.log(util.inspect(message, { showHidden: false, depth: null })) : "");
        this.onUniquenessError = (error) => {
            console.error(`UniquenessError: ${error.message}`);
            process.exit(1);
        };
    }
}

export default SignalingChannel;
