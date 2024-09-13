using NativeCollections;

namespace enet
{
    public struct ENetServer
    {
        public int State;
        public NativeArray<nint> Peers;
        public NativeConcurrentQueue<nint> RemovedPeers;
        public NativeConcurrentQueue<ENetEvent> Incomings;
        public NativeConcurrentQueue<ENetOutgoing> Outgoings;
    }
}