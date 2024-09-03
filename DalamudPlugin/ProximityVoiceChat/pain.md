This is a retrospective of my process for finding the tech stack needed for this plugin.

Since Discord doesn't have an API for adjusting individual user volumes in a voice call, I decided this plugin should have voice chat capabilities.
After some light research, it was clear that WebRTC would be the way to go for networking.
The way this works is that each peer in the voice call would be connected via websocket to every other peer in the call, and that socket would transmit the audio packets between the peers.
To establish these p2p sockets, a deployed signaling server would receive and relay room join events.
Once a peer connected to the signaling server, they would receive the information of everyone else in the voice room, and then could transmit details needed to setup the p2p sockets through the signaling server.
These details include SDP and ICE data, which contain information for how to traverse router firewalls and NAT.
Once these sockets are established, any arbitrary byte array data can be sent through them, and the goal was to send microphone audio packets.

As a quick aside, there's another method for implementing conference calling behavior, and that's sending all audio data to a central server, where multiple audio streams are mixed together on the server and then sent as one stream back to every peer.
I opted to not do this as each client would need to change the volumes of each other peer very responsively, so I could not accept the latency that came with using a central server to perform the volume mixing.

To start, I needed to find some examples of conference call implementations. Since WebRTC is mainly used in web applications, many examples are in Javascript.
https://github.com/Meshiest/demo-proximity-voice
https://github.com/aljanabim/simple_webrtc_signaling_server

Since Dalamud is in C#, my first through was to find a C# wrapper of the WebRTC implementation. Here were the options:
https://github.com/sipsorcery-org/sipsorcery
https://github.com/microsoft/MixedReality-WebRTC
https://github.com/microsoft/winrtc

Out of these 3, the two Microsoft repos had not been updated in years, while SIPSocery was still under active development. So I chose to start with SIPSocery.

Since SIPSorcery didn't come with a signaling server implementation, I first needed to make a signaling server. Since the WebRTC protocol is just a standard that should be compatible across different platforms, I just largely copied the implementation in https://github.com/aljanabim/simple_webrtc_signaling_server.
This had the added benefit of being able to use the sample client code to test peer connections.
SIPSorcery had an API for getting microphone audio samples, which used SDL2. This mostly worked, although for some reason my Steinberg UR12 DAC would stop recording without error. I worked around this by using NVIDIA Broadcast (RTX Voice) as my SDL input source.
Implementing SIPSorcery's WebRTC methods though, came with a lot of troubles. Essentially copying the simple_webrtc client code, multiple issues would come up. SIPSorcery didn't seem to generate compatible SDP details to connect to the sample client, and simply initializing an RTCPeerConnection would throw some uncaught Socket exception in Dalamud, despite all my efforts to wrap every asynchronous method in try/catch.
Even in a test with two instances of Dalamud, a peer connection could not be established.

So, my next line of thinking was to more accurately replicate the working sample code, so I aimed to run a Nodejs environment in the C# plugin. The options to do this were
https://github.com/microsoft/node-api-dotnet
https://github.com/agracio/edge-js
except Edge.js is not a real option since it only works for .NET Framework 4.5 (the plugin is on .NET Core 8.0)

With Node API for .NET, I was able to embed a Nodejs environment into the C# plugin, but there was one massive issue, and it was that the code to load the Nodejs environment could only be ran once per application launch, and any more after that would crash the application. In this case, the application is FFXIV.
This limitation would make plugin development hell, as reloading a plugin would lose the reference to the previously instantiated Nodejs environment.
So, I looked for ways to keep a reference to this loaded environment.
One thought was to make a separate plugin that would never be unloaded and would instantiate the Nodejs environment. However, it ended up proving impossible to access the instantiated class across plugins.
Some insane things I tried and learned:
- You can't GCHandle.Alloc & pin classes with reference fields, since those reference fields aren't pinned and can move around in memory.
- The same assembly loaded into different AssemblyLoadContexts will be treated as different assemblies, meaning you cannot cast a typed class to an object in one context, and then cast it back to the original object in another context.
- Dalamud gives a new context for every plugin loaded, as well as reloaded, so you cannot use Dalamud's DataShare to keep an instanced variable reference.

Some other ideas I considered but deemed way too hard/time consuming to do:
- Build a custom version of Dalamud with the Nodejs environment loader built-in
- Continue down the path of using a separate plugin for the Nodejs environment, but use Reflection and dynamic methods on the Nodejs environment object
- Use https://github.com/aiortc/aiortc in an embedded python environment

Ultimately, I went back to the Microsoft MixedReality-WebRTC, and rewriting the WebRTCManager using their implementation of PeerConnection, I got things working with the sample Nodejs clients!
Kind of embarrassing the solution was in front of me this whole time, but going down rabbit holes is my specialty.
