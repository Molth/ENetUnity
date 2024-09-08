using NativeCollections;

namespace enet
{
    public sealed class ENetServer
    {
        public int State;
        public NativeDictionary<int, nint> Peers;
        public NativeConcurrentQueue<nint> RemovedPeers;
        public NativeConcurrentQueue<ENetEvent> Incomings;
        public NativeConcurrentQueue<ENetOutgoing> Outgoings;
    }
}