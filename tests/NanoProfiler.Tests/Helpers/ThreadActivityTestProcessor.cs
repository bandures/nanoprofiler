using System.Collections.Concurrent;

namespace NanoProfiler.Tests.Helpers
{
    class ThreadActivityTestProcessor :
        IProcessor<Protocol.ScopeBegin>,
        IProcessor<Protocol.ScopeEnd>,
        IProcessor<Protocol.ActivityBegin>,
        IProcessor<Protocol.ActivityEnd>
    {
        public record struct Record(
            int ThreadId,
            string Type,
            uint MarkerId,
            int UniqueId = 0
        );

        public readonly ConcurrentQueue<Record> Records = new();

        public void HandleMessage(int threadId, ref Protocol.ScopeBegin msg) => 
            Records.Enqueue(new Record(threadId, "ScopeBegin", msg.MarkerId));

        public void HandleMessage(int threadId, ref Protocol.ScopeEnd msg) => 
            Records.Enqueue(new Record(threadId, "ScopeEnd", msg.MarkerId));

        public void HandleMessage(int threadId, ref Protocol.ActivityBegin msg) => 
            Records.Enqueue(new Record(threadId, "ActivityBegin", msg.MarkerId, msg.UniqueId));

        public void HandleMessage(int threadId, ref Protocol.ActivityEnd msg) => 
            Records.Enqueue(new Record(threadId, "ActivityEnd", msg.MarkerId, msg.UniqueId));

        public void Dispose() {}
    }
}
