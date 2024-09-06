#if UNITY_2021_3_OR_NEWER || GODOT
#endif
using static enet.ENet;

namespace enet
{
    /// <summary>
    ///     Network outgoing
    /// </summary>
    public unsafe struct NetworkOutgoing
    {
        /// <summary>
        ///     Peer
        /// </summary>
        public ENetPeer* Peer;

        /// <summary>
        ///     DataPacket
        /// </summary>
        public ENetPacket* Packet;

        /// <summary>
        ///     Structure
        /// </summary>
        /// <param name="peer">Peer</param>
        /// <param name="data">DataPacket</param>
        public NetworkOutgoing(ENetPeer* peer, ENetPacket* data)
        {
            Peer = peer;
            Packet = data;
        }

        /// <summary>
        ///     Create
        /// </summary>
        /// <param name="peer">Peer</param>
        /// <param name="data">DataPacket</param>
        /// <param name="length">Length</param>
        /// <param name="flag">Flag</param>
        /// <returns>NetworkOutgoing</returns>
        public static NetworkOutgoing Create(ENetPeer* peer, byte* data, int length, ENetPacketFlag flag)
        {
            var packet = enet_packet_create(data, length, (uint)flag);
            return new NetworkOutgoing(peer, packet);
        }
    }
}