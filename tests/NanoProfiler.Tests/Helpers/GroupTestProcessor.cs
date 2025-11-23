using System.Collections.Generic;

namespace Meowtron.NanoProfiler.Tests.Helpers
{
    /// <summary>
    /// Fake processor that registers all group infos
    /// </summary>
    class GroupTestProcessor : INanoProcessor<NanoProtocol.RegisterGroup>
    {
        public readonly List<NanoProtocol.RegisterGroup> Groups = new ();
        public void HandleMessage(int threadId, ref NanoProtocol.RegisterGroup msg) => Groups.Add(msg);
        public void Dispose()
        {
        }
    }
}