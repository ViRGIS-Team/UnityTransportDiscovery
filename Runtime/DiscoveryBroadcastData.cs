
namespace Unity.Netcode.Community.Discovery {
    public struct DiscoveryBroadcastData : INetworkSerializable
    {
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
        }
    }
}