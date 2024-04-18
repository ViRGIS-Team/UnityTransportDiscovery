# This Package

This package provides a simple but effective LAN wide broadcast server discovery mechanism for Unity Netcode for Gameobjects.

The package is a fork of the Unity [Netcode for GameObjects Extensions Network Discovery package](https://github.com/Unity-Technologies/multiplayer-community-contributions/tree/main/com.community.netcode.extensions).

It has been made into a standalone package and has been updated for more recent versions of Netcode for GameObjects.

The example code has been moved to a seperate test project to avoid overstuffing this package and keep things simple.

# NetworkDiscovery

Network discovery allows clients to find active game servers in the same local area network without going through the hassle of figuring out the servers IP and passing it to other player.

Under the hood network discovery uses UDP broadcasts to send a broadcast to all devices on the LAN to which active servers will respond to.

> Note: Network discovery only works on LAN and might not work in some networks.

TBD => Tidy up the resy

## ExampleNetworkDiscovery & ExampleNetworkDiscoveryHud

The `ExampleNetworkDiscovery` class provides an example implementation for implementing network discovery.

To use the `ExampleNetworkDiscovery`:
1. add the `ExampleNetworkDiscovery` and the `ExampleNetworkDiscoveryHud` components to the GameObject which contains the `NetworkManager.
2. Run your application start a server or host. A "Stop Server Discovery" button should appear on the left side of the screen. This indicates that the server is discoverable. The button can be pressed to disable or enable the discovery of the server.
3. Run your application but don't start a client yet. On the left side of the screen a "Discover Servers" button should appear. Press that button and any servers which are discoverable will be listed. Press on one of those servers to join the server as a client.

## Writing your own network discovery

Take a look at `ExampleNetworkDiscovery` for how to write your own NetworkDiscovery. `DiscoveryBroadcastData` and `DiscoveryResponseData` can be filled with more information if needed on a case per case basis. For instance the server could include a number of current players in the response.