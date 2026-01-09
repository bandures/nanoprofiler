using NanoProfiler;
using System.Collections.Generic;

namespace Meowtron.NanoProfiler.Tests.Helpers
{
    /// <summary>
    /// Fake processor that registers all marker infos
    /// </summary>
    class MarkerTestProcessor : IProcessor<Protocol.RegisterMarker>
    {
        public readonly List<Protocol.RegisterMarker> Markers = new ();
        public void HandleMessage(int threadId, ref Protocol.RegisterMarker msg) => Markers.Add(msg);
        public void Dispose()
        {
        }
    }
}