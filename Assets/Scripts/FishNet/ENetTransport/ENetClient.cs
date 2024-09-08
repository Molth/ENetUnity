using NativeCollections;

namespace enet
{
    public sealed class ENetClient
    {
        public int State;
        public nint Peer;
        public NativeConcurrentQueue<ENetEvent> Incomings;
        public NativeConcurrentQueue<ENetOutgoing> Outgoings;
    }
}