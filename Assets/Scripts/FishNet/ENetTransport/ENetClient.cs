using NativeCollections;

namespace enet
{
    public struct ENetClient
    {
        public int State;
        public nint Peer;
        public NativeConcurrentQueue<ENetEvent> Incomings;
        public NativeConcurrentQueue<ENetOutgoing> Outgoings;
    }
}