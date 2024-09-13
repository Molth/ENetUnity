using System;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using enet;
using NativeCollections;
using UnityEngine;
using static enet.ENet;

namespace Mirror
{
    public unsafe class ENetTransport : Transport, PortTransport
    {
        public const int MAX_PACKET_LENGTH = 4096;
        public string Address;
        private int _state;
        private bool _isServer;
        private NativeConcurrentQueue<nint> _removedPeers;
        private NativeConcurrentQueue<ENetOutgoing> _outgoings;
        private NativeConcurrentQueue<ENetEvent> _incomings;
        private NativeArray<nint> _peers;
        private nint _peer;
        private byte[] _receiveBuffer;

        private void Start() => _receiveBuffer = new byte[MAX_PACKET_LENGTH];

        public ushort Port { get; set; } = 7777;

        ~ENetTransport() => Shutdown();

        private void StartServer()
        {
            _isServer = true;
            _peers = new NativeArray<nint>(NetworkManager.singleton.maxConnections, true);
            _removedPeers = new NativeConcurrentQueue<nint>(1, 2);
            _outgoings = new NativeConcurrentQueue<ENetOutgoing>(1, 2);
            _incomings = new NativeConcurrentQueue<ENetEvent>(1, 2);
            enet_initialize();
            ENetHost* host = null;
            try
            {
                var address = new ENetAddress();
                enet_set_ip(&address, Socket.OSSupportsIPv6 ? "::0" : "0.0.0.0");
                address.port = Port;
                host = enet_host_create(&address, NetworkManager.singleton.maxConnections, 0, 0, 0);
                var @event = new ENetEvent();
                var spinCount = 0;
                Interlocked.Exchange(ref _state, 1);
                while (_state == 1)
                {
                    while (_removedPeers.TryDequeue(out var peer))
                        enet_peer_disconnect_now((ENetPeer*)peer, 0);
                    while (_outgoings.TryDequeue(out var outgoing))
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

                        _incomings.Enqueue(@event);
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
                    foreach (var peer in _peers)
                    {
                        if (peer != IntPtr.Zero)
                            enet_peer_disconnect_now((ENetPeer*)peer, 0);
                    }

                    enet_host_flush(host);
                    enet_host_destroy(host);
                }

                _removedPeers.Dispose();
                while (_outgoings.TryDequeue(out var outgoing))
                    enet_packet_destroy(outgoing.Packet);
                _outgoings.Dispose();
                while (_incomings.TryDequeue(out var networkEvent))
                    enet_packet_destroy(networkEvent.packet);
                _incomings.Dispose();
                _peers.Dispose();
                _isServer = false;
                enet_deinitialize();
            }
        }

        private void StartClient()
        {
            _peer = IntPtr.Zero;
            _outgoings = new NativeConcurrentQueue<ENetOutgoing>(1, 2);
            _incomings = new NativeConcurrentQueue<ENetEvent>(1, 2);
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
                Interlocked.Exchange(ref _state, 2);
                while (_state == 2)
                {
                    while (_outgoings.TryDequeue(out var outgoing))
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

                        _incomings.Enqueue(@event);
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
                    if (_peer != IntPtr.Zero)
                        enet_peer_disconnect_now((ENetPeer*)_peer, 0);
                    enet_host_flush(host);
                    enet_host_destroy(host);
                }

                while (_outgoings.TryDequeue(out var outgoing))
                    enet_packet_destroy(outgoing.Packet);
                _outgoings.Dispose();
                while (_incomings.TryDequeue(out var networkEvent))
                    enet_packet_destroy(networkEvent.packet);
                _incomings.Dispose();
                enet_deinitialize();
            }
        }

        public override void ServerEarlyUpdate()
        {
            if (_state == 1)
            {
                while (_incomings.TryDequeue(out var networkEvent))
                {
                    var peer = networkEvent.peer;
                    switch (networkEvent.type)
                    {
                        case ENetEventType.ENET_EVENT_TYPE_NONE:
                            break;
                        case ENetEventType.ENET_EVENT_TYPE_CONNECT:
                            _peers[peer->incomingPeerID] = (nint)peer;
                            OnServerConnectedWithAddress(peer->incomingPeerID + 1, peer->address.ToString());
                            break;
                        case ENetEventType.ENET_EVENT_TYPE_DISCONNECT:
                            _peers[peer->incomingPeerID] = IntPtr.Zero;
                            OnServerDisconnected(peer->incomingPeerID + 1);
                            break;
                        case ENetEventType.ENET_EVENT_TYPE_RECEIVE:
                            var length = networkEvent.packet->dataLength;
                            if (length > MAX_PACKET_LENGTH)
                                enet_packet_destroy(networkEvent.packet);
                            else
                            {
                                Unsafe.CopyBlock(ref _receiveBuffer[0], ref *networkEvent.packet->data, (uint)length);
                                var channel = (networkEvent.packet->flags & (uint)ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE) != 0 ? Channels.Reliable : Channels.Unreliable;
                                enet_packet_destroy(networkEvent.packet);
                                OnServerDataReceived(peer->incomingPeerID + 1, new ArraySegment<byte>(_receiveBuffer, 0, (int)length), channel);
                            }

                            break;
                    }
                }
            }
            else if (_state == 2)
            {
                while (_incomings.TryDequeue(out var networkEvent))
                {
                    var peer = networkEvent.peer;
                    switch (networkEvent.type)
                    {
                        case ENetEventType.ENET_EVENT_TYPE_NONE:
                            break;
                        case ENetEventType.ENET_EVENT_TYPE_CONNECT:
                            _peer = (nint)peer;
                            OnClientConnected();
                            break;
                        case ENetEventType.ENET_EVENT_TYPE_DISCONNECT:
                            _peer = IntPtr.Zero;
                            OnClientDisconnected?.Invoke();
                            break;
                        case ENetEventType.ENET_EVENT_TYPE_RECEIVE:
                            var length = networkEvent.packet->dataLength;
                            if (length > MAX_PACKET_LENGTH)
                                enet_packet_destroy(networkEvent.packet);
                            else
                            {
                                Unsafe.CopyBlock(ref _receiveBuffer[0], ref *networkEvent.packet->data, (uint)length);
                                var channel = (networkEvent.packet->flags & (uint)ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE) != 0 ? Channels.Reliable : Channels.Unreliable;
                                enet_packet_destroy(networkEvent.packet);
                                OnClientDataReceived(new ArraySegment<byte>(_receiveBuffer, 0, (int)length), channel);
                            }

                            break;
                    }
                }
            }
        }

        public override bool Available() => Application.platform != RuntimePlatform.WebGLPlayer;

        public override bool ClientConnected() => _state == 2;

        public override void ClientConnect(string address)
        {
            if (address == "localhost")
                address = "127.0.0.1";
            Address = address;
            new Thread(StartClient) { IsBackground = true }.Start();
        }

        public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
            if (_peer != IntPtr.Zero)
                _outgoings.Enqueue(ENetOutgoing.Create(_peer, segment, channelId == Channels.Reliable ? ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE : ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED));
        }

        public override void ClientDisconnect()
        {
            if (!_isServer)
                Interlocked.Exchange(ref _state, 0);
        }

        public override Uri ServerUri() => null;

        public override bool ServerActive() => _state == 1;

        public override void ServerStart() => new Thread(StartServer) { IsBackground = true }.Start();

        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
            var peer = _peers[connectionId - 1];
            if (peer != IntPtr.Zero)
                _outgoings.Enqueue(ENetOutgoing.Create(peer, segment, channelId == Channels.Reliable ? ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE : ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED));
        }

        public override void ServerDisconnect(int connectionId)
        {
            var peer = _peers[connectionId - 1];
            if (peer != IntPtr.Zero)
                _removedPeers.Enqueue(peer);
        }

        public override string ServerGetClientAddress(int connectionId) => null;

        public override void ServerStop() => Interlocked.Exchange(ref _state, 0);

        public override int GetMaxPacketSize(int channelId = Channels.Reliable) => (int)ENET_HOST_DEFAULT_MTU;

        public override void Shutdown() => Interlocked.Exchange(ref _state, 0);
    }
}