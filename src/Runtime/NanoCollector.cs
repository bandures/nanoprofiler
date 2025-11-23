using System;
using System.Threading;
using System.Collections.Generic;

namespace Meowtron.NanoProfiler
{
    /// <summary>
    /// Collector aggregates results from all message pipes into a single stream and
    /// passes it to Processors for handling and output. 
    ///  
    /// Collector doesn't implement any specific format and relies on registered
    /// Processors to serializer and/or visualize incoming data.
    /// </summary>
    internal class NanoCollector : IDisposable
    {
        struct ThreadProfiler
        {
            public int ThreadId;
            public INanoSerializer DataSource;
            public ManualResetEventSlim ReceivedByCollectorSignal;
        }
        
        private readonly Thread _thread;
        private readonly CancellationTokenSource _cancellationTokenSource;
        
        private readonly List<ThreadProfiler> _profilers;
        private readonly List<INanoProcessorBase> _processors;
        
        private readonly ManualResetEventSlim _dataReadySignal;

        public NanoCollector(IEnumerable<NanoThreadLocalProfiler> threads, IEnumerable<INanoProcessorBase> processors, ManualResetEventSlim dataReadySignal)
        {
            _processors = new List<INanoProcessorBase>(processors);

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
                    ReceivedByCollectorSignal = thread.ReceivedByCollectorSignal
                });
            }
            
            // Signal to process messages immediately after start
            _dataReadySignal.Set();
            
            _thread = new Thread(() => ThreadCollect(_cancellationTokenSource.Token))  { IsBackground = true, Name = "NanoProfiler.Collector" };
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
        
        public void RegisterThreadProfiler(int threadId, INanoSerializer dataSource, ManualResetEventSlim receivedByCollectorSignal)
        {
            lock(_profilers)
            {
                _profilers.Add(new ThreadProfiler() { ThreadId = threadId, DataSource = dataSource, ReceivedByCollectorSignal = receivedByCollectorSignal });
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
            // To prevent race condition if new thread is started.
            // This lock shouldn't be a problem as new thread creation
            // is a rare event
            lock (_profilers)
            {
                foreach(ThreadProfiler profilerThread in _profilers)
                {
                    try
                    {
                        HandleThread(profilerThread);
                    }
                    catch (Exception e)
                    {
                        // TODO: Should we report errors back to the profiler?
                        Console.WriteLine(e);
                    }
                }
            }
        }
        
        private void HandleThread(ThreadProfiler profilerThread)
        {
            while(profilerThread.DataSource.Advance())
            {
                var msgType = NanoProtocol.MessageType.None;
                profilerThread.DataSource.Transfer(ref msgType);

                // All known message types must be registered here.
                // It's done this way to avoid allocations on protocol
                // message conversion to interface.
                switch (msgType)
                {
                    case NanoProtocol.MessageType.CaptureStart:
                        HandleMessage<NanoProtocol.CaptureStart>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case NanoProtocol.MessageType.CaptureStop:
                        HandleMessage<NanoProtocol.CaptureStop>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case NanoProtocol.MessageType.Frame:
                        HandleMessage<NanoProtocol.Frame>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case NanoProtocol.MessageType.RegisterGroup:
                        HandleMessage<NanoProtocol.RegisterGroup>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case NanoProtocol.MessageType.RegisterMarker:
                        HandleMessage<NanoProtocol.RegisterMarker>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case NanoProtocol.MessageType.ScopeBegin:
                        HandleMessage<NanoProtocol.ScopeBegin>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case NanoProtocol.MessageType.ScopeEnd:
                        HandleMessage<NanoProtocol.ScopeEnd>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case NanoProtocol.MessageType.CounterSample:
                        HandleMessage<NanoProtocol.CounterSample>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case NanoProtocol.MessageType.ActivityBegin:
                        HandleMessage<NanoProtocol.ActivityBegin>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case NanoProtocol.MessageType.ActivityEnd:
                        HandleMessage<NanoProtocol.ActivityEnd>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                    case NanoProtocol.MessageType.Flush:
                        HandleMessage<NanoProtocol.Flush>(profilerThread.ThreadId, profilerThread.DataSource);
                        break;
                                
                    case NanoProtocol.MessageType.None:
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            
            // Collector read all data sources, and they're ready
            // to receive again.
            profilerThread.ReceivedByCollectorSignal.Set();
        }
        
        private void HandleMessage<T>(int threadId, INanoSerializer reader) where T: struct, NanoProtocol.IMessage
        {
            T message = default;
            message.Transfer(reader);

            foreach (INanoProcessorBase processorBase in _processors)
            {
                if (processorBase is INanoProcessor<T> processor)
                    processor.HandleMessage(threadId, ref message);
            }
        }
    }
}