using NanoProfiler;
using System.Collections.Generic;

namespace Meowtron.NanoProfiler.Tests.Helpers
{
    /// <summary>
    /// Fake processor that registers all group infos
    /// </summary>
    class GroupTestProcessor : IProcessor<Protocol.RegisterGroup>
    {
        public readonly List<Protocol.RegisterGroup> Groups = new ();
        public void HandleMessage(int threadId, ref Protocol.RegisterGroup msg) => Groups.Add(msg);
        public void Dispose()
        {
        }
    }
}