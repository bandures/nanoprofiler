using System.Collections.Generic;

namespace Meowtron.NanoProfiler.Tests.Helpers
{
    /// <summary>
    /// Fake processor that registers all activity start and end events
    /// </summary>
    class ActivityTestProcessor :
        INanoProcessor<NanoProtocol.ActivityBegin>,
        INanoProcessor<NanoProtocol.ActivityEnd>
    {
        public record struct ActivityRecord(
            bool Start,
            uint MarkerId,
            int UniqueId,
            TypedAttribute param1,
            TypedAttribute param2,
            TypedAttribute param3);
        public readonly List<ActivityRecord> ActivityRecords = new ();
        public void HandleMessage(int threadId, ref NanoProtocol.ActivityBegin msg) => ActivityRecords.Add(
            new ActivityRecord(true, msg.MarkerId, msg.UniqueId, msg.Param1, msg.Param2, msg.Param3));
        public void HandleMessage(int threadId, ref NanoProtocol.ActivityEnd msg) => ActivityRecords.Add(
            new ActivityRecord(false, msg.MarkerId, msg.UniqueId, default, default, default));
        public void Dispose()
        {
        }
    }
}