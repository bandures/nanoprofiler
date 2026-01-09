namespace NanoProfiler
{
    /// <summary>
    /// Profiler internal message exchange protocol
    /// </summary>
    public static class Protocol
    {
        public enum MessageType : byte
        {
            None = 0,

            CaptureStart,
            CaptureStop,

            Frame,
            
            RegisterMarker,
            
            ScopeBegin,
            ScopeEnd,
            
            CounterSample,
            
            ActivityBegin,
            ActivityEnd,
            
            RegisterGroup,
            
            Flush,
        }

        public interface IMessage
        {
            public void Transfer(ISerializer serializer);
        }

        public struct MessageHeader : IMessage
        {
            public MessageType MsgType;

            public void Transfer(ISerializer serializer)
            {
                serializer.Transfer(ref MsgType);
                serializer.Align();
            }
        }

        public struct CaptureStart : IMessage
        {
            public int ProcessId;
            public long Timestamp;

            public void Transfer(ISerializer serializer)
            {
                serializer.Transfer(ref ProcessId);
                serializer.Transfer(ref Timestamp);
            }
        }

        public struct CaptureStop : IMessage
        {
            public long Timestamp;

            public void Transfer(ISerializer serializer)
            {
                serializer.Transfer(ref Timestamp);
            }
        }

        public struct Frame : IMessage
        {
            public const uint kInvalidFrame = unchecked((uint)-1);

            public uint Index;
            public long Timestamp;

            public void Transfer(ISerializer serializer)
            {
                serializer.Transfer(ref Index);
                serializer.Transfer(ref Timestamp);
            }
        }

        public struct Flush : IMessage
        {
            public void Transfer(ISerializer serializer)
            {
            }
        }
        
        public struct RegisterMarker : IMessage
        {
            public uint MarkerId;
            public uint GroupId;
            public string Name;
            public MarkerFlags Flags;

            public void Transfer(ISerializer serializer)
            {
                serializer.Transfer(ref MarkerId);
                serializer.Transfer(ref GroupId);
                serializer.Transfer(ref Name);
                serializer.Transfer(ref Flags);
            }
        }

        public struct ScopeBegin : IMessage
        {
            public uint MarkerId;
            public long Timestamp;

            public void Transfer(ISerializer serializer)
            {
                serializer.Transfer(ref MarkerId);
                serializer.Transfer(ref Timestamp);
            }
        }

        public struct ScopeEnd : IMessage
        {
            public uint MarkerId;
            public long Timestamp;

            public void Transfer(ISerializer serializer)
            {
                serializer.Transfer(ref MarkerId);
                serializer.Transfer(ref Timestamp);
            }
        }

        public struct CounterSample : IMessage
        {
            public uint MarkerId;
            public long Timestamp;
            public float Value;

            public void Transfer(ISerializer serializer)
            {
                serializer.Transfer(ref MarkerId);
                serializer.Transfer(ref Timestamp);
                serializer.Transfer(ref Value);
            }
        }
        
        public struct ActivityBegin : IMessage
        {
            public uint MarkerId;
            public int UniqueId;
            public long Timestamp;
            public TypedAttribute Param1;
            public TypedAttribute Param2;
            public TypedAttribute Param3;

            public void Transfer(ISerializer serializer)
            {
                serializer.Transfer(ref MarkerId);
                serializer.Transfer(ref UniqueId);
                serializer.Transfer(ref Timestamp);
                Param1.Transfer(serializer);
                Param2.Transfer(serializer);
                Param3.Transfer(serializer);
            }
        }

        public struct ActivityEnd : IMessage
        {
            public uint MarkerId;
            public int UniqueId;
            public long Timestamp;

            public void Transfer(ISerializer serializer)
            {
                serializer.Transfer(ref MarkerId);
                serializer.Transfer(ref UniqueId);
                serializer.Transfer(ref Timestamp);
            }
        }

        public struct RegisterGroup : IMessage
        {
            public uint GroupId;
            public string Name;
            public MarkerFlags Flags;

            public void Transfer(ISerializer serializer)
            {
                serializer.Transfer(ref GroupId);
                serializer.Transfer(ref Name);
                serializer.Transfer(ref Flags);
            }
        }
    }    
}
