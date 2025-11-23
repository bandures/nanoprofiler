namespace Meowtron.NanoProfiler.Tests.Helpers
{
    /// <summary>
    /// Fake processor that can count number of messages received by type
    /// </summary>
    class CountMessagesTestProcessor : 
        INanoProcessor<NanoProtocol.CaptureStart>,
        INanoProcessor<NanoProtocol.CaptureStop>,
        INanoProcessor<NanoProtocol.RegisterMarker>,
        INanoProcessor<NanoProtocol.RegisterGroup>,
        INanoProcessor<NanoProtocol.ScopeBegin>,
        INanoProcessor<NanoProtocol.ScopeEnd>,
        INanoProcessor<NanoProtocol.CounterSample>,
        INanoProcessor<NanoProtocol.ActivityBegin>,
        INanoProcessor<NanoProtocol.ActivityEnd>
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
            
        public void HandleMessage(int threadId, ref NanoProtocol.CaptureStart msg) => CaptureStart++;
        public void HandleMessage(int threadId, ref NanoProtocol.CaptureStop msg) => CaptureStop++;
            
        public void HandleMessage(int threadId, ref NanoProtocol.RegisterMarker msg) => MarkerInfoCount++;
            
        public void HandleMessage(int threadId, ref NanoProtocol.ScopeBegin msg) => ScopeBeginCount++;
        public void HandleMessage(int threadId, ref NanoProtocol.ScopeEnd msg) => ScopeEndCount++;
            
        public void HandleMessage(int threadId, ref NanoProtocol.CounterSample msg) => CounterSamples++;
            
        public void HandleMessage(int threadId, ref NanoProtocol.ActivityBegin msg) => ActivityBeginCount++;
        public void HandleMessage(int threadId, ref NanoProtocol.ActivityEnd msg) => ActivityEndCount++;

        public void HandleMessage(int threadId, ref NanoProtocol.RegisterGroup msg) => GroupInfoCount++;

        public void Dispose()
        {
        }
    }
}