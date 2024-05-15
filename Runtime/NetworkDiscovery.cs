using System;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Unity.Netcode.Community.Discovery {
    [DisallowMultipleComponent]
    public abstract class NetworkDiscovery<TBroadCast, TResponse> : MonoBehaviour
        where TBroadCast : INetworkSerializable, new()
        where TResponse : INetworkSerializable, new()
    {
        private enum MessageType : byte
        {
            BroadCast = 0,
            Response = 1,
        }

        UdpClient m_Client;

        [SerializeField] ushort portRangeStart = 47770;
        [SerializeField] int portRange = 10;

        // This is long because unity inspector does not like ulong.
        [SerializeField]
        long m_UniqueApplicationId;

        /// <summary>
        /// Gets a value indicating whether the discovery is running.
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Gets whether the discovery is in server mode.
        /// </summary>
        public bool IsServer { get; private set; }

        /// <summary>
        /// Gets whether the discovery is in client mode.
        /// </summary>
        public bool IsClient { get; private set; }

        public void OnApplicationQuit()
        {
            StopDiscovery();
        }

        void OnValidate()
        {
            if (m_UniqueApplicationId == 0)
            {
                var value1 = (long) Random.Range(int.MinValue, int.MaxValue);
                var value2 = (long) Random.Range(int.MinValue, int.MaxValue);
                m_UniqueApplicationId = value1 + (value2 << 32);
            }
        }

        public void ClientBroadcast(TBroadCast broadCast)
        {
            if (!IsClient)
            {
                throw new InvalidOperationException("Cannot send client broadcast while not running in client mode. Call StartClient first.");
            }



            using (FastBufferWriter writer = new FastBufferWriter(1024, Allocator.Temp, 1024 * 64))
            {
                
                WriteHeader(writer, MessageType.BroadCast);

                writer.WriteNetworkSerializable(broadCast);
                var data = writer.ToArray();

                try
                {
                    // This works because PooledBitStream.Get resets the position to 0 so the array segment will always start from 0.

                    // Send broadcast requests to all ip addresses in the range

                    for (int i = 0; i < portRange; i++)
                    {
                        IPEndPoint endPoint = new(IPAddress.Broadcast, (ushort)(portRangeStart + i));
                        m_Client.SendAsync(data, data.Length, endPoint);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }
        }

        /// <summary>
        /// Starts the discovery in server mode which will respond to client broadcasts searching for servers.
        /// </summary>
        public virtual void StartServer()
        {
            StartDiscovery(true);
        }

        /// <summary>
        /// Starts the discovery in client mode. <see cref="ClientBroadcast"/> can be called to send out broadcasts to servers and the client will actively listen for responses.
        /// </summary>
        public virtual void StartClient()
        {
            StartDiscovery(false);
        }

        public virtual void StopDiscovery()
        {
            IsClient = false;
            IsServer = false;
            IsRunning = false;

            if (m_Client != null)
            {
                try
                {
                    m_Client.Close();
                }
                catch (Exception)
                {
                    // We don't care about socket exception here. Socket will always be closed after this.
                }

                m_Client = null;
            }
        }

        /// <summary>
        /// Gets called whenever a broadcast is received. Creates a response based on the incoming broadcast data.
        /// </summary>
        /// <param name="sender">The sender of the broadcast</param>
        /// <param name="broadCast">The broadcast data which was sent</param>
        /// <param name="response">The response to send back</param>
        /// <returns>True if a response should be sent back else false</returns>
        protected abstract bool ProcessBroadcast(IPEndPoint sender, TBroadCast broadCast, out TResponse response);

        /// <summary>
        /// Gets called when a response to a broadcast gets received
        /// </summary>
        /// <param name="sender">The sender of the response</param>
        /// <param name="response">The value of the response</param>
        protected abstract void ResponseReceived(IPEndPoint sender, TResponse response);

        void StartDiscovery(bool isServer)
        {
            StopDiscovery();

            IsServer = isServer;
            IsClient = !isServer;


            // If we are not a server we use the 0 port (let udp client assign a free port to us)
            ushort port = 0;

            // for a server search the Port range to look for a free port
            try
            {
                if (IsServer)
                {
                    if (portRange == 1)
                    {
                        // If port range is zero - fix the part to the defined value regardless - this reproduces the legacy behaviour
                        port = portRangeStart;
                    }
                    else
                    {
                        IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
                        IPEndPoint[] UDPendpoints = properties.GetActiveUdpListeners();
                        for (int i = 0; i < portRange; i++)
                        {
                            port = (ushort)(portRangeStart + i);
                            if (Array.Find<IPEndPoint>(UDPendpoints, ep =>
                            {
                                return ep.Port == port;
                            }) == null) break;
                        }
                    }
                }
                m_Client = new UdpClient(port) { EnableBroadcast = true, MulticastLoopback = false };
            }
            catch (NotImplementedException)
            {
                for (int i = 0; i < portRange; i++)
                {
                    port = (ushort)(portRangeStart + i);
                    try
                    {
                        m_Client = new UdpClient(port) { EnableBroadcast = true, MulticastLoopback = false };
                    }
                    catch (Exception)
                    {
                        // do nothing - assuming this is a port clash
                        continue;
                    }
                    // if we get here - it worked
                    break;
                }
            }
            _ = ListenAsync(isServer ? ReceiveBroadcastAsync : new Func<Task>(ReceiveResponseAsync));

            IsRunning = true;

            Debug.Log($"NetworkDiscovery : {(IsServer ? "Server" : "Client")} started on port {port}");
        }

        async Task ListenAsync(Func<Task> onReceiveTask)
        {
            while (true)
            {
                try
                {
                    await onReceiveTask();
                }
                catch (ObjectDisposedException)
                {
                    // socket has been closed
                    break;
                }
                catch (Exception)
                {
                }
            }
        }

        async Task ReceiveResponseAsync()
        {
            UdpReceiveResult udpReceiveResult = await m_Client.ReceiveAsync();

            var segment = new ArraySegment<byte>(udpReceiveResult.Buffer, 0, udpReceiveResult.Buffer.Length);
            using var reader = new FastBufferReader(segment, Allocator.Persistent);

            try
            {
                if (ReadAndCheckHeader(reader, MessageType.Response) == false)
                {
                    return;
                }
                
                reader.ReadNetworkSerializable(out TResponse receivedResponse);
                ResponseReceived(udpReceiveResult.RemoteEndPoint, receivedResponse);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        async Task ReceiveBroadcastAsync()
        {
            UdpReceiveResult udpReceiveResult = await m_Client.ReceiveAsync();

            var segment = new ArraySegment<byte>(udpReceiveResult.Buffer, 0, udpReceiveResult.Buffer.Length);
            using var reader = new FastBufferReader(segment, Allocator.Persistent);

            try
            {
                if (ReadAndCheckHeader(reader, MessageType.BroadCast) == false)
                {
                    return;
                }
                
                reader.ReadNetworkSerializable(out TBroadCast receivedBroadcast);

                if (ProcessBroadcast(udpReceiveResult.RemoteEndPoint, receivedBroadcast, out TResponse response))
                {
                    using var writer = new FastBufferWriter(1024, Allocator.Persistent, 1024 * 64);
                    WriteHeader(writer, MessageType.Response);

                    writer.WriteNetworkSerializable(response);
                    var data = writer.ToArray();

                    await m_Client.SendAsync(data, data.Length, udpReceiveResult.RemoteEndPoint);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        
        private void WriteHeader(FastBufferWriter writer, MessageType messageType)
        {
            // Serialize unique application id to make sure packet received is from same application.
            writer.WriteValueSafe(m_UniqueApplicationId);

            // Write a flag indicating whether this is a broadcast
            writer.WriteByteSafe((byte) messageType);
        }

        private bool ReadAndCheckHeader(FastBufferReader reader, MessageType expectedType)
        {
            reader.ReadValueSafe(out long receivedApplicationId);
            if (receivedApplicationId != m_UniqueApplicationId)
            {
                return false;
            }

            reader.ReadByteSafe(out byte messageType);
            if (messageType != (byte) expectedType)
            {
                return false;
            }

            return true;
        }
    }
}