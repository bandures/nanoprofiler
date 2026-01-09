using System;

namespace NanoProfiler
{
    /// <summary>
    /// Base type for all profiler output data processors
    /// </summary>
    public interface IProcessorBase : IDisposable
    {
    }

    /// <summary>
    /// Processor declares this interface for each message type
    /// it can handle.
    /// </summary>
    /// <typeparam name="T">See <seealso cref="Protocol" /> for all message types</typeparam>
    public interface IProcessor<T> : IProcessorBase where T: Protocol.IMessage
    {
        public void HandleMessage(int threadId, ref T msg);
    }
}