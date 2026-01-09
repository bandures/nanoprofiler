using NanoProfiler;

namespace Meowtron.NanoProfiler.Tests.Helpers
{
    /// <summary>
    /// Fake processor that can count number of messages received by type
    /// </summary>
    class CountMessagesTestProcessor : 
        IProcessor<Protocol.CaptureStart>,
        IProcessor<Protocol.CaptureStop>,
        IProcessor<Protocol.RegisterMarker>,
        IProcessor<Protocol.RegisterGroup>,
        IProcessor<Protocol.ScopeBegin>,
        IProcessor<Protocol.ScopeEnd>,
        IProcessor<Protocol.CounterSample>,
        IProcessor<Protocol.ActivityBegin>,
        IProcessor<Protocol.ActivityEnd>
    {
        public int CaptureStart;
        public int CaptureStop;
            
        public int GroupInfoCount;
        public int MarkerInfoCount;
            
        public int ScopeBeginCount;
        public int ScopeEndCount;
            
        public int CounterSamples;
            
        public int ActivityBeginCount;
        public int ActivityEndCount;
            
        public void HandleMessage(int threadId, ref Protocol.CaptureStart msg) => CaptureStart++;
        public void HandleMessage(int threadId, ref Protocol.CaptureStop msg) => CaptureStop++;
            
        public void HandleMessage(int threadId, ref Protocol.RegisterMarker msg) => MarkerInfoCount++;
            
        public void HandleMessage(int threadId, ref Protocol.ScopeBegin msg) => ScopeBeginCount++;
        public void HandleMessage(int threadId, ref Protocol.ScopeEnd msg) => ScopeEndCount++;
            
        public void HandleMessage(int threadId, ref Protocol.CounterSample msg) => CounterSamples++;
            
        public void HandleMessage(int threadId, ref Protocol.ActivityBegin msg) => ActivityBeginCount++;
        public void HandleMessage(int threadId, ref Protocol.ActivityEnd msg) => ActivityEndCount++;

        public void HandleMessage(int threadId, ref Protocol.RegisterGroup msg) => GroupInfoCount++;

        public void Dispose()
        {
        }
    }
}