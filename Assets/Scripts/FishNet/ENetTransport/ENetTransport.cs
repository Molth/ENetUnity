using System;
using System.Runtime.CompilerServices;
using System.Threading;
using enet;
using FishNet.Transporting;
using NativeCollections;
using static enet.ENet;

namespace FishNet
{
    public sealed unsafe class ENetTransport : Transport
    {
        public const int MAX_PACKET_LENGTH = 4096;
        public string Address;
        public ushort Port;
        public int MaxPeers = 100;
        private ENetServer _server;
        private ENetClient _client;
        private byte[] _receiveBuffer;

        private void Start()
        {
            _server ??= new ENetServer();
            _client ??= new ENetClient();
            _receiveBuffer = new byte[MAX_PACKET_LENGTH];
        }

        private void OnDestroy() => Shutdown();
        ~ENetTransport() => Shutdown();

        public override void SetServerBindAddress(string address, IPAddressType addressType)
        {
        }

        public override void SetMaximumClients(int value) => MaxPeers = value;

        public override int GetMaximumClients() => MaxPeers;

        public override string GetServerBindAddress(IPAddressType addressType) => "0.0.0.0";

        public override string GetClientAddress() => Address;

        public override void SetClientAddress(string address) => Address = address;

        public override void SetPort(ushort port) => Port = port;

        public override ushort GetPort() => Port;

        public override string GetConnectionAddress(int connectionId) => _server.Peers.TryGetValue(connectionId, out var peer) ? ((ENetPeer*)peer)->address.host.ToString() : null;

        public override event Action<ClientConnectionStateArgs> OnClientConnectionState;
        public override event Action<ServerConnectionStateArgs> OnServerConnectionState;
        public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;

        public override void HandleClientConnectionState(ClientConnectionStateArgs connectionStateArgs) => OnClientConnectionState?.Invoke(connectionStateArgs);

        public override void HandleServerConnectionState(ServerConnectionStateArgs connectionStateArgs) => OnServerConnectionState?.Invoke(connectionStateArgs);

        public override void HandleRemoteConnectionState(RemoteConnectionStateArgs connectionStateArgs) => OnRemoteConnectionState?.Invoke(connectionStateArgs);

        public override LocalConnectionState GetConnectionState(bool server)
        {
            if (server)
                return _server.State != 0 ? LocalConnectionState.Started : LocalConnectionState.Stopped;
            if (_client.Peer != IntPtr.Zero)
            {
                switch (((ENetPeer*)_client.Peer)->state)
                {
                    case ENetPeerState.ENET_PEER_STATE_CONNECTED:
                        return LocalConnectionState.Started;
                    case ENetPeerState.ENET_PEER_STATE_DISCONNECTED:
                        return LocalConnectionState.Stopped;
                    case ENetPeerState.ENET_PEER_STATE_CONNECTING:
                    case ENetPeerState.ENET_PEER_STATE_ACKNOWLEDGING_CONNECT:
                    case ENetPeerState.ENET_PEER_STATE_CONNECTION_PENDING:
                    case ENetPeerState.ENET_PEER_STATE_CONNECTION_SUCCEEDED:
                        return LocalConnectionState.Starting;
                    case ENetPeerState.ENET_PEER_STATE_DISCONNECT_LATER:
                    case ENetPeerState.ENET_PEER_STATE_DISCONNECTING:
                    case ENetPeerState.ENET_PEER_STATE_ACKNOWLEDGING_DISCONNECT:
                    case ENetPeerState.ENET_PEER_STATE_ZOMBIE:
                    default:
                        return LocalConnectionState.Stopping;
                }
            }

            return LocalConnectionState.Stopped;
        }

        public override RemoteConnectionState GetConnectionState(int connectionId)
        {
            if (_server.Peers.TryGetValue(connectionId, out var peer))
            {
                switch (((ENetPeer*)peer)->state)
                {
                    case ENetPeerState.ENET_PEER_STATE_CONNECTED:
                        return RemoteConnectionState.Started;
                    case ENetPeerState.ENET_PEER_STATE_DISCONNECTED:
                    case ENetPeerState.ENET_PEER_STATE_CONNECTING:
                    case ENetPeerState.ENET_PEER_STATE_ACKNOWLEDGING_CONNECT:
                    case ENetPeerState.ENET_PEER_STATE_CONNECTION_PENDING:
                    case ENetPeerState.ENET_PEER_STATE_CONNECTION_SUCCEEDED:
                    case ENetPeerState.ENET_PEER_STATE_DISCONNECT_LATER:
                    case ENetPeerState.ENET_PEER_STATE_DISCONNECTING:
                    case ENetPeerState.ENET_PEER_STATE_ACKNOWLEDGING_DISCONNECT:
                    case ENetPeerState.ENET_PEER_STATE_ZOMBIE:
                    default:
                        return RemoteConnectionState.Stopped;
                }
            }

            return RemoteConnectionState.Stopped;
        }

        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            var channel = (Channel)channelId;
            if (_client.Peer != IntPtr.Zero)
            {
                var outgoing = ENetOutgoing.Create(_client.Peer, segment, channel == Channel.Reliable ? ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE : ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED);
                _client.Outgoings.Enqueue(outgoing);
            }
        }

        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            var channel = (Channel)channelId;
            if (_server.Peers.TryGetValue(connectionId, out var peer))
            {
                var outgoing = ENetOutgoing.Create(peer, segment, channel == Channel.Reliable ? ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE : ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED);
                _server.Outgoings.Enqueue(outgoing);
            }
        }

        public override event Action<ClientReceivedDataArgs> OnClientReceivedData;

        public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs receivedDataArgs) => OnClientReceivedData?.Invoke(receivedDataArgs);

        public override event Action<ServerReceivedDataArgs> OnServerReceivedData;

        public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs receivedDataArgs) => OnServerReceivedData?.Invoke(receivedDataArgs);

        public override void IterateIncoming(bool server)
        {
            if (server)
            {
                if (_server == null || _server.State == 0)
                    return;
                while (_server.Incomings.TryDequeue(out var networkEvent))
                {
                    var peer = networkEvent.peer;
                    switch (networkEvent.type)
                    {
                        case ENetEventType.ENET_EVENT_TYPE_NONE:
                            HandleServerConnectionState(new ServerConnectionStateArgs(LocalConnectionState.Started, Index));
                            break;
                        case ENetEventType.ENET_EVENT_TYPE_CONNECT:
                            _server.Peers[peer->incomingPeerID] = (nint)peer;
                            HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Started, peer->incomingPeerID, Index));
                            break;
                        case ENetEventType.ENET_EVENT_TYPE_DISCONNECT:
                            _server.Peers.Remove(peer->incomingPeerID);
                            HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Stopped, peer->incomingPeerID, Index));
                            break;
                        case ENetEventType.ENET_EVENT_TYPE_RECEIVE:
                            var length = networkEvent.packet->dataLength;
                            if (length > MAX_PACKET_LENGTH)
                                enet_packet_destroy(networkEvent.packet);
                            else
                            {
                                Unsafe.CopyBlock(ref _receiveBuffer[0], ref *networkEvent.packet->data, (uint)length);
                                var channel = (networkEvent.packet->flags & (uint)ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE) != 0 ? Channel.Reliable : Channel.Unreliable;
                                enet_packet_destroy(networkEvent.packet);
                                HandleServerReceivedDataArgs(new ServerReceivedDataArgs(new ArraySegment<byte>(_receiveBuffer, 0, (int)length), channel, peer->incomingPeerID, Index));
                            }

                            break;
                    }
                }
            }
            else
            {
                if (_client == null || _client.State == 0)
                    return;
                while (_client.Incomings.TryDequeue(out var networkEvent))
                {
                    var peer = networkEvent.peer;
                    switch (networkEvent.type)
                    {
                        case ENetEventType.ENET_EVENT_TYPE_NONE:
                            break;
                        case ENetEventType.ENET_EVENT_TYPE_CONNECT:
                            _client.Peer = (nint)peer;
                            HandleClientConnectionState(new ClientConnectionStateArgs(LocalConnectionState.Started, Index));
                            break;
                        case ENetEventType.ENET_EVENT_TYPE_DISCONNECT:
                            _client.Peer = IntPtr.Zero;
                            HandleClientConnectionState(new ClientConnectionStateArgs(LocalConnectionState.Stopped, Index));
                            break;
                        case ENetEventType.ENET_EVENT_TYPE_RECEIVE:
                            var length = networkEvent.packet->dataLength;
                            if (length > MAX_PACKET_LENGTH)
                                enet_packet_destroy(networkEvent.packet);
                            else
                            {
                                Unsafe.CopyBlock(ref _receiveBuffer[0], ref *networkEvent.packet->data, (uint)length);
                                var channel = (networkEvent.packet->flags & (uint)ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE) != 0 ? Channel.Reliable : Channel.Unreliable;
                                enet_packet_destroy(networkEvent.packet);
                                HandleClientReceivedDataArgs(new ClientReceivedDataArgs(new ArraySegment<byte>(_receiveBuffer, 0, (int)length), channel, Index));
                            }

                            break;
                    }
                }
            }
        }

        public override void IterateOutgoing(bool server)
        {
        }

        public override bool StartConnection(bool server)
        {
            if (server)
            {
                if (_server.State != 0)
                    return false;
                new Thread(StartServer) { IsBackground = true }.Start();
            }
            else
            {
                if (_client.State != 0)
                    return false;
                new Thread(StartClient) { IsBackground = true }.Start();
                HandleClientConnectionState(new ClientConnectionStateArgs(LocalConnectionState.Starting, Index));
            }

            return true;
        }

        private void StartServer()
        {
            _server.Peers = new NativeDictionary<int, nint>(MaxPeers);
            _server.RemovedPeers = new NativeConcurrentQueue<nint>(1, 2);
            _server.Outgoings = new NativeConcurrentQueue<ENetOutgoing>(1, 2);
            _server.Incomings = new NativeConcurrentQueue<ENetEvent>(1, 2);
            enet_initialize();
            ENetHost* host = null;
            try
            {
                var address = new ENetAddress();
                enet_set_ip(&address, "0.0.0.0");
                address.port = Port;
                host = enet_host_create(&address, MaxPeers, 0, 0, 0);
                var @event = new ENetEvent();
                _server.Incomings.Enqueue(@event);
                var spinCount = 0;
                Interlocked.Exchange(ref _server.State, 1);
                while (_server.State == 1)
                {
                    while (_server.RemovedPeers.TryDequeue(out var peer))
                        enet_peer_disconnect_now((ENetPeer*)peer, 0);
                    while (_server.Outgoings.TryDequeue(out var outgoing))
                    {
                        if (enet_peer_send((ENetPeer*)outgoing.Peer, 0, outgoing.Packet) != 0)
                            enet_packet_destroy(outgoing.Packet);
                    }

                    var polled = false;
                    while (!polled)
                    {
                        if (enet_host_check_events(host, &@event) <= 0)
                        {
                            if (enet_host_service(host, &@event, 1) <= 0)
                                break;
                            polled = true;
                        }

                        if (@event.type == ENetEventType.ENET_EVENT_TYPE_NONE)
                            continue;
                        if (@event.type == ENetEventType.ENET_EVENT_TYPE_CONNECT)
                        {
                            var peer = @event.peer;
                            enet_peer_ping_interval(peer, 500);
                            enet_peer_timeout(peer, 5000, 0, 0);
                        }

                        _server.Incomings.Enqueue(@event);
                    }

                    enet_host_flush(host);
                    if ((spinCount >= 10 && (spinCount - 10) % 2 == 0) || Environment.ProcessorCount == 1)
                    {
                        var yieldsSoFar = spinCount >= 10 ? (spinCount - 10) / 2 : spinCount;
                        if (yieldsSoFar % 5 == 4)
                            Thread.Sleep(0);
                        else
                            Thread.Yield();
                    }
                    else
                    {
                        var iterations = Environment.ProcessorCount / 2;
                        if (spinCount <= 30 && 1 << spinCount < iterations)
                            iterations = 1 << spinCount;
                        Thread.SpinWait(iterations);
                    }

                    spinCount = spinCount == int.MaxValue ? 10 : spinCount + 1;
                }
            }
            finally
            {
                if (host != null)
                {
                    foreach (var peer in _server.Peers.Values)
                        enet_peer_disconnect_now((ENetPeer*)peer, 0);
                    enet_host_flush(host);
                    enet_host_destroy(host);
                }

                _server.RemovedPeers.Dispose();
                while (_server.Outgoings.TryDequeue(out var outgoing))
                    enet_packet_destroy(outgoing.Packet);
                _server.Outgoings.Dispose();
                while (_server.Incomings.TryDequeue(out var networkEvent))
                    enet_packet_destroy(networkEvent.packet);
                _server.Incomings.Dispose();
                _server.Peers.Dispose();
                enet_deinitialize();
            }
        }

        private void StartClient()
        {
            _client.Peer = IntPtr.Zero;
            _client.Outgoings = new NativeConcurrentQueue<ENetOutgoing>(1, 2);
            _client.Incomings = new NativeConcurrentQueue<ENetEvent>(1, 2);
            enet_initialize();
            ENetHost* host = null;
            try
            {
                var address = new ENetAddress();
                enet_set_ip(&address, Address);
                address.port = Port;
                host = enet_host_create(null, 1, 0, 0, 0);
                var peer = enet_host_connect(host, &address, 0, 0);
                enet_peer_ping_interval(peer, 500);
                enet_peer_timeout(peer, 5000, 0, 0);
                var @event = new ENetEvent();
                var spinCount = 0;
                Interlocked.Exchange(ref _client.State, 2);
                while (_client.State == 2)
                {
                    while (_client.Outgoings.TryDequeue(out var outgoing))
                    {
                        if (enet_peer_send((ENetPeer*)outgoing.Peer, 0, outgoing.Packet) != 0)
                            enet_packet_destroy(outgoing.Packet);
                    }

                    var polled = false;
                    while (!polled)
                    {
                        if (enet_host_check_events(host, &@event) <= 0)
                        {
                            if (enet_host_service(host, &@event, 1) <= 0)
                                break;
                            polled = true;
                        }

                        if (@event.type == ENetEventType.ENET_EVENT_TYPE_NONE)
                            continue;
                        if (@event.type == ENetEventType.ENET_EVENT_TYPE_CONNECT)
                        {
                            peer = @event.peer;
                            enet_peer_ping_interval(peer, 500);
                            enet_peer_timeout(peer, 5000, 0, 0);
                        }

                        _client.Incomings.Enqueue(@event);
                    }

                    enet_host_flush(host);
                    if ((spinCount >= 10 && (spinCount - 10) % 2 == 0) || Environment.ProcessorCount == 1)
                    {
                        var yieldsSoFar = spinCount >= 10 ? (spinCount - 10) / 2 : spinCount;
                        if (yieldsSoFar % 5 == 4)
                            Thread.Sleep(0);
                        else
                            Thread.Yield();
                    }
                    else
                    {
                        var iterations = Environment.ProcessorCount / 2;
                        if (spinCount <= 30 && 1 << spinCount < iterations)
                            iterations = 1 << spinCount;
                        Thread.SpinWait(iterations);
                    }

                    spinCount = spinCount == int.MaxValue ? 10 : spinCount + 1;
                }
            }
            finally
            {
                if (host != null)
                {
                    if (_client.Peer != IntPtr.Zero)
                        enet_peer_disconnect_now((ENetPeer*)_client.Peer, 0);
                    enet_host_flush(host);
                    enet_host_destroy(host);
                }

                while (_client.Outgoings.TryDequeue(out var outgoing))
                    enet_packet_destroy(outgoing.Packet);
                _client.Outgoings.Dispose();
                while (_client.Incomings.TryDequeue(out var networkEvent))
                    enet_packet_destroy(networkEvent.packet);
                _client.Incomings.Dispose();
                enet_deinitialize();
            }
        }

        public override bool StopConnection(bool server)
        {
            if (server)
            {
                if (_server == null)
                    return true;
                if (Interlocked.CompareExchange(ref _server.State, 0, 1) == 1)
                    HandleServerConnectionState(new ServerConnectionStateArgs(LocalConnectionState.Stopped, Index));
            }
            else
            {
                if (_client == null)
                    return true;
                if (Interlocked.CompareExchange(ref _client.State, 0, 2) == 2)
                    HandleClientConnectionState(new ClientConnectionStateArgs(LocalConnectionState.Stopped, Index));
            }

            return true;
        }

        public override bool StopConnection(int connectionId, bool immediately)
        {
            if (immediately)
            {
                if (_server.Peers.Remove(connectionId, out var peer))
                {
                    _server.RemovedPeers.Enqueue(peer);
                    return true;
                }
            }
            else
            {
                if (_server.Peers.TryGetValue(connectionId, out var peer))
                {
                    _server.RemovedPeers.Enqueue(peer);
                    return true;
                }
            }

            return false;
        }

        public override void Shutdown()
        {
            StopConnection(false);
            StopConnection(true);
        }

        public override int GetMTU(byte channel) => 1023;
    }
}