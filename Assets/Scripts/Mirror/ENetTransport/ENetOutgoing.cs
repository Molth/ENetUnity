#if UNITY_2021_3_OR_NEWER || GODOT
using System;
#endif
using static enet.ENet;

namespace enet
{
    /// <summary>
    ///     ENet outgoing
    /// </summary>
    public unsafe struct ENetOutgoing
    {
        /// <summary>
        ///     Peer
        /// </summary>
        public nint Peer;

        /// <summary>
        ///     DataPacket
        /// </summary>
        public ENetPacket* Packet;

        /// <summary>
        ///     Structure
        /// </summary>
        /// <param name="peer">Peer</param>
        /// <param name="data">DataPacket</param>
        public ENetOutgoing(nint peer, ENetPacket* data)
        {
            Peer = peer;
            Packet = data;
        }

        /// <summary>
        ///     Create
        /// </summary>
        /// <param name="peer">Peer</param>
        /// <param name="data">DataPacket</param>
        /// <param name="flag">Flag</param>
        /// <returns>NetworkOutgoing</returns>
        public static ENetOutgoing Create(nint peer, Span<byte> data, ENetPacketFlag flag)
        {
            ENetPacket* packet;
            fixed (byte* ptr = &data[0])
            {
                packet = enet_packet_create(ptr, data.Length, (uint)flag);
            }

            return new ENetOutgoing(peer, packet);
        }
    }
}