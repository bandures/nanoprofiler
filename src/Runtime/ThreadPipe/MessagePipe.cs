using System;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NanoProfiler
{
    /// <summary>
    /// Single producer, single consumer thread safe message pipe.
    /// </summary>
    /// <remarks>
    /// Message pipe is based on a ring buffer and doesn't allocate.
    /// - Producer and consumer can be on different threads
    /// - Events are atomic, so the event passes or fails as whole
    /// - The pipe never blocks, it either can or can't send the message
    /// - If there is not enough space in the buffer to handle the message
    ///   the pipe fails for the message with return code (no exception is thrown)
    /// - The pipe has a signal it triggers when message is put into the pipe
    ///
    /// |xxxxxwwwwwwwwwwwwwww----------rrrrrrrrrrxxxx|
    ///       ^              ^         ^         ^
    ///    _writePosition    |         |         |
    ///          writer._writePosition |         |
    ///                           _readPosition  |
    ///                                    reader._readPosition
    ///
    /// x - valid data
    /// w - written, but not advanced by the writer
    /// - - empty
    /// r - read, but not advanced by the reader
    /// </remarks>
    internal sealed class MessagePipe : IDisposable
    {
        private readonly IntPtr _buffer;
        private readonly int _capacity;
        private readonly int _capacityMask;

        private readonly ReadSerializer _readSerializer;
        private readonly WriteSerializer _writeSerializer;
        private int _readPosition;
        private int _writePosition;

        private long _transferredBytes;
        private long _discardedBytes;

        public MessagePipe(int capacity)
        {
            if ((capacity & (capacity - 1)) != 0)
                throw new ArgumentException($"{nameof(MessagePipe)} size should be power of two.");

            _buffer = Marshal.AllocHGlobal(capacity);
            _capacity = capacity;
            _capacityMask = capacity - 1;

            _readPosition = 0;
            _writePosition = 0;
            _writeSerializer = new WriteSerializer(this);
            _readSerializer = new ReadSerializer(this);
        }

        ~MessagePipe()
        {
            Dispose(false);
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        private void Dispose(bool disposing)
        {
            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
            }
        }
        
        /// <summary>
        /// Pipe message reader 
        /// </summary>
        public ISerializer Reader => _readSerializer;

        public ISerializer Writer => _writeSerializer;

        public void GetStatistics(out long transferredBytes, out long discardedBytes)
        {
            transferredBytes = _transferredBytes;
            discardedBytes = _discardedBytes;
        }

        private unsafe Span<byte> AsSpan()
        {
            return new Span<byte>(_buffer.ToPointer(), _capacity);
        }

        private class ReadSerializer : ISerializer
        {
            private readonly MessagePipe _buffer;
            private readonly int _alignment;
            private int _readPos;
            private int _endPos;

            internal ReadSerializer(MessagePipe buffer)
            {
                _buffer = buffer;
                _readPos = Volatile.Read(ref _buffer._readPosition);
                _endPos = _readPos;
                _alignment = 4;
            }

            public float Contention => (float)WrapBufferPos(_buffer, _endPos - _readPos) / _buffer._capacity;
            
            public void Transfer<T>(ref T value) where T : unmanaged
            {
                Span<T> valueSpan = MemoryMarshal.CreateSpan(ref value, 1);
                Span<byte> byteSpan = MemoryMarshal.AsBytes(valueSpan);
                Read(byteSpan);
            }

            public void Transfer(ref string value)
            {
                int size = 0;
                Transfer(ref size);

                unsafe
                {
                    byte* buffer = stackalloc byte[size];
                    Span<byte> bufferSpan = new Span<byte>(buffer, size);
                    Read(bufferSpan);

                    value = System.Text.Encoding.Unicode.GetString(bufferSpan);
                }
            }

            public void Align()
            {
                _readPos = WrapBufferPos(_buffer, unchecked((_readPos + (_alignment - 1)) & (-_alignment)));
            }

            public bool Advance()
            {
                // advance to the end of the last message
                Volatile.Write(ref _buffer._readPosition, _endPos);
                
                var writePosition = Volatile.Read(ref _buffer._writePosition);
                if (Volatile.Read(ref _buffer._readPosition) == writePosition)
                    return false;

                // read message header byte-size
                Span<byte> span = _buffer.AsSpan().Slice(Volatile.Read(ref _buffer._readPosition), 1);
                ref byte messageSize = ref span[0];
                
                // update message boundaries for the currently read message 
                _readPos = WrapBufferPos(_buffer, Volatile.Read(ref _buffer._readPosition) + sizeof(byte));
                _endPos = WrapBufferPos(_buffer, Volatile.Read(ref _buffer._readPosition) + messageSize);
                return true;
            }

            private void Read(Span<byte> dstSpan)
            {
                int dstSize = dstSpan.Length;

                int availableToRead = WrapBufferPos(_buffer, _endPos - _readPos);
                if (availableToRead < dstSize)
                    return;
                
                Span<byte> bufferSpan = _buffer.AsSpan();
                int continuousSpace = Min(_buffer._capacity - _readPos, availableToRead);
                if (continuousSpace < dstSize)
                {
                    // split copy at buffer boundary 
                    Span<byte> srcSpan1 = bufferSpan.Slice(_readPos, continuousSpace);
                    Span<byte> dstSpan1 = dstSpan.Slice(0, continuousSpace);
                    srcSpan1.CopyTo(dstSpan1);
                        
                    Span<byte> srcSpan2 = bufferSpan.Slice(0, dstSize - continuousSpace);
                    Span<byte> dstSpan2 = dstSpan.Slice(continuousSpace);
                    srcSpan2.CopyTo(dstSpan2);
                }
                else
                {
                    Span<byte> srcSpan = bufferSpan.Slice(_readPos, dstSize);
                    srcSpan.CopyTo(dstSpan);
                }
                
                _readPos = WrapBufferPos(_buffer, _readPos + dstSize);
            }
        }

        private class WriteSerializer : ISerializer
        {
            private readonly MessagePipe _buffer;
            private readonly int _alignment;
            private int _writePos;
            private int _messageSize;
            private bool _failed;

            internal WriteSerializer(MessagePipe buffer)
            {
                _buffer = buffer;
                _writePos = WrapBufferPos(_buffer, Volatile.Read(ref _buffer._writePosition) + sizeof(byte));
                _failed = false;
                _alignment = 4;
            }

            public float Contention => (float)(WrapBufferPos(_buffer, _writePos - Volatile.Read(ref _buffer._readPosition)) + 1) / _buffer._capacity;
            
            public void Transfer<T>(ref T value) where T : unmanaged
            {
                ReadOnlySpan<T> valueSpan = MemoryMarshal.CreateReadOnlySpan(ref value, 1);
                ReadOnlySpan<byte> byteSpan = MemoryMarshal.AsBytes(valueSpan); 
                Write(byteSpan);
            }

            public void Transfer(ref string value)
            {
                ReadOnlySpan<byte> valueSpan = MemoryMarshal.AsBytes(value.AsSpan());

                int size = valueSpan.Length;
                Transfer(ref size);

                Write(valueSpan);
            }

            public void Align()
            {
                _writePos = WrapBufferPos(_buffer,unchecked((_writePos + (_alignment - 1)) & (-_alignment)));
            }

            public bool Advance()
            {
                if (_failed)
                {
                    Interlocked.Add(ref _buffer._discardedBytes, _messageSize);
                    Restart();
                    return false;
                }

                var size = WrapBufferPos(_buffer, _writePos - Volatile.Read(ref _buffer._writePosition));
                if (size > 255)
                {
                    Interlocked.Add(ref _buffer._discardedBytes, _messageSize);
                    Restart();
                    return false;
                }

                Span<byte> span = _buffer.AsSpan().Slice(Volatile.Read(ref _buffer._writePosition), 1);
                span[0] = (byte)size;
                
                Volatile.Write(ref _buffer._writePosition, _writePos);
                Interlocked.Add(ref _buffer._transferredBytes, _messageSize);
                Restart();

                return true;
            }

            private void Write(ReadOnlySpan<byte> srcSpan)
            {
                var srcSize = srcSpan.Length;
                _messageSize += srcSize;

                var availableToWrite = _buffer._capacity - WrapBufferPos(_buffer, _writePos - Volatile.Read(ref _buffer._readPosition)) - 1;
                if (_failed || availableToWrite < srcSize)
                {
                    _failed = true;
                    return;
                }

                Span<byte> bufferSpan = _buffer.AsSpan();
                int continuousSpace = Min(_buffer._capacity - _writePos, availableToWrite);
                if (continuousSpace < srcSize)
                {
                    // split copy at buffer boundary 
                    Span<byte> dstSpan1 = bufferSpan.Slice(_writePos, continuousSpace);
                    ReadOnlySpan<byte> srcSpan1 = srcSpan.Slice(0, continuousSpace);
                    srcSpan1.CopyTo(dstSpan1);

                    Span<byte> dstSpan2 = bufferSpan.Slice(0, srcSize - continuousSpace);
                    ReadOnlySpan<byte> srcSpan2 = srcSpan.Slice(continuousSpace);
                    srcSpan2.CopyTo(dstSpan2);
                }
                else
                {
                    Span<byte> dstSpan = bufferSpan.Slice(_writePos, srcSize);
                    srcSpan.CopyTo(dstSpan);
                }
                
                _writePos = WrapBufferPos(_buffer, _writePos + srcSize);
            }

            private void Restart()
            {
                _failed = false;
                _writePos = Volatile.Read(ref _buffer._writePosition);
                    
                // Try to reserve space for message size header.
                // If no space is available, entire message will be
                // marked as failed
                byte messageSize = 0;
                Transfer(ref messageSize);
                
                _messageSize = 0;
            }
        }

        // Implemented here for inlining 
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Min(int l, int r) => l < r ? l : r;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int WrapBufferPos(MessagePipe buffer, int pos) => pos & buffer._capacityMask;
    }
}