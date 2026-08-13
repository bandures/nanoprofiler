using System;
using System.Threading;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace NanoProfiler
{
    /// <summary>
    /// Collector aggregates results from all message pipes into a single stream and
    /// passes it to Processors for handling and output. 
    ///  
    /// Collector doesn't implement any specific format and relies on registered
    /// Processors to serializer and/or visualize incoming data.
    /// </summary>
    internal class Collector : IDisposable
    {
        private struct ThreadProfiler
        {
            public int ThreadId;
            public ISerializer DataSource;
            public ThreadLocalProfiler LocalProfiler;
        }

        private readonly ILogger _logger;
        
        private readonly Thread _thread;
        private readonly CancellationTokenSource _cancellationTokenSource;
        
        private readonly List<ThreadProfiler> _profilers;
        private readonly List<IProcessorBase> _processors;
        
        private readonly ManualResetEventSlim _dataReadySignal;

        public Collector(IEnumerable<ThreadLocalProfiler> threads, IEnumerable<IProcessorBase> processors, ManualResetEventSlim dataReadySignal, ILogger logger)
        {
            _logger = logger;
            
            _processors = new List<IProcessorBase>(processors);

            _dataReadySignal = dataReadySignal;
            _cancellationTokenSource = new CancellationTokenSource();

            // Add all known threads to collect from
            _profilers = new List<ThreadProfiler>();
            foreach (var thread in threads)
            {
                _profilers.Add(new ThreadProfiler()
                {
                    ThreadId = thread.ThreadId,
                    DataSource = thread.MessageReader,
                    LocalProfiler = thread
                });
            }
            
            // Signal to process messages immediately after start
            _dataReadySignal.Set();
            
            _thread = new Thread(() => ThreadCollect(_cancellationTokenSource.Token))  { IsBackground = true, Name = "Profiler.Collector" };
            _thread.Start();
        }
        
        public void Dispose()
        {
            // Signal termination & wakeup thread
            _cancellationTokenSource.Cancel();
            _dataReadySignal.Set();
            _thread.Join();
            
            _cancellationTokenSource?.Dispose();
        }
        
        public void RegisterThreadProfiler(ThreadLocalProfiler localProfiler)
        {
            lock(_profilers)
            {
                _profilers.Add(new ThreadProfiler { ThreadId = localProfiler.ThreadId, DataSource = localProfiler.MessageReader, LocalProfiler = localProfiler });
            }
            
            _dataReadySignal.Set();
        }

        private void ThreadCollect(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Wait for any thread profiler to signal that new data is available
                    _dataReadySignal.Wait(cancellationToken);
                    _dataReadySignal.Reset();

                    HandleAllThreads();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            
            HandleAllThreads();
        }

        private void HandleAllThreads()
        {
            // To prevent race condition if a new thread is started.
            // This lock shouldn't be a problem as new thread creation
            // is a rare event
            lock (_profilers)
            {
                foreach(var profilerThread in _profilers)
                {
                    try
                    {
                        HandleThread(profilerThread);
                    }
                    catch (Exception e)
                    {
                        _logger?.LogError(e, $"Failed processing messages from thread {profilerThread.ThreadId}");
                    }
                }
            }
        }
        
        private void HandleThread(ThreadProfiler profilerThread)
        {
            while(profilerThread.DataSource.Advance())
            {
                var msgType = Protocol.MessageType.None;
                profilerThread.DataSource.Transfer(ref msgType);

                // All known message types must be registered here.
                // It's done this way to avoid allocations on protocol
                // message conversion to interface.
                switch (msgType)
                {
                    case Protocol.MessageType.CaptureStart:
                        HandleMessage<Protocol.CaptureStart>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case Protocol.MessageType.CaptureStop:
                        HandleMessage<Protocol.CaptureStop>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case Protocol.MessageType.Frame:
                        HandleMessage<Protocol.Frame>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case Protocol.MessageType.RegisterGroup:
                        HandleMessage<Protocol.RegisterGroup>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case Protocol.MessageType.RegisterMarker:
                        HandleMessage<Protocol.RegisterMarker>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case Protocol.MessageType.ScopeBegin:
                        HandleMessage<Protocol.ScopeBegin>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case Protocol.MessageType.ScopeEnd:
                        HandleMessage<Protocol.ScopeEnd>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case Protocol.MessageType.CounterSample:
                        HandleMessage<Protocol.CounterSample>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case Protocol.MessageType.ActivityBegin:
                        HandleMessage<Protocol.ActivityBegin>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case Protocol.MessageType.ActivityEnd:
                        HandleMessage<Protocol.ActivityEnd>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case Protocol.MessageType.Event:
                        HandleMessage<Protocol.Event>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;

                    // Fence is handled directly here as it affects the thread profiler state
                    case Protocol.MessageType.Fence:
                    {
                        Protocol.Fence message = new ();
                        message.Transfer(profilerThread.DataSource);
                        profilerThread.LocalProfiler.SetFenceCompleted(message.FenceId);
                        break;
                    }
                                
                    case Protocol.MessageType.None:
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        
        private void HandleMessage<T>(int threadId, ISerializer reader) where T: struct, Protocol.IMessage
        {
            T message = default;
            message.Transfer(reader);

            foreach (IProcessorBase processorBase in _processors)
            {
                if (processorBase is IProcessor<T> processor)
                    processor.HandleMessage(threadId, ref message);
            }
        }
    }
}