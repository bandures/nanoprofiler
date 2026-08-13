using System.Collections.Generic;

namespace NanoProfiler.Tests.Helpers
{
    /// <summary>
    /// Fake processor that registers all point in time events
    /// </summary>
    class EventTestProcessor : IProcessor<Protocol.Event>
    {
        public record struct EventRecord(
            uint MarkerId,
            long Timestamp,
            TypedAttribute param1,
            TypedAttribute param2,
            TypedAttribute param3);
        public readonly List<EventRecord> EventRecords = new ();
        public void HandleMessage(int threadId, ref Protocol.Event msg) => EventRecords.Add(
            new EventRecord(msg.MarkerId, msg.Timestamp, msg.Param1, msg.Param2, msg.Param3));
        public void Dispose()
        {
        }
    }
}
