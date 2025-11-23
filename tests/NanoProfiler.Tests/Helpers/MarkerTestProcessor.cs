using System.Collections.Generic;

namespace Meowtron.NanoProfiler.Tests.Helpers
{
    /// <summary>
    /// Fake processor that registers all marker infos
    /// </summary>
    class MarkerTestProcessor : INanoProcessor<NanoProtocol.RegisterMarker>
    {
        public readonly List<NanoProtocol.RegisterMarker> Markers = new ();
        public void HandleMessage(int threadId, ref NanoProtocol.RegisterMarker msg) => Markers.Add(msg);
        public void Dispose()
        {
        }
    }
}