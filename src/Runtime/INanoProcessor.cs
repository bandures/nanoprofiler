using System;

namespace Meowtron.NanoProfiler
{
    /// <summary>
    /// Base type for all profiler output data processors
    /// </summary>
    public interface INanoProcessorBase : IDisposable
    {
    }

    /// <summary>
    /// Processor declares this interface for each message type
    /// it can handle.
    /// </summary>
    /// <typeparam name="T">See <seealso cref="NanoProtocol" /> for all message types</typeparam>
    public interface INanoProcessor<T> : INanoProcessorBase where T: NanoProtocol.IMessage
    {
        public void HandleMessage(int threadId, ref T msg);
    }
}