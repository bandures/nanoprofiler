#nullable enable
using System;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace Meowtron.NanoProfiler
{
    public class PerfettoFrameRecorder : IDisposable
    {
        private readonly int _bufferSize;
        private readonly ILogger? _logger;
        
        private IntPtr _buffer;
        private int _position;
        private bool _valid;

        public PerfettoFrameRecorder(int bufferSize, ILogger? logger)
        {
            if (bufferSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferSize));
            
            _bufferSize = bufferSize;
            _logger = logger;
            _buffer = Marshal.AllocHGlobal(bufferSize);
            _position = 0;
            _valid = true;
        }

        public bool IsValid => _valid;
        public int Position => _position;

        public void Dispose()
        {
            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero;
            }
            _valid = false;
        }

        public void Write<T>(ref T value) where T : unmanaged
        {
            if (!_valid)
                return;
            
            var valueSpan = MemoryMarshal.CreateReadOnlySpan(ref value, 1);
            var srcSpan = MemoryMarshal.AsBytes(valueSpan);
            if (_position + srcSpan.Length > _bufferSize)
            {
                _valid = false;
                _logger?.LogWarning("FrameRecorder buffer overflow. Increase buffer size.");
                return;
            }
            
            var dstSpan = BufferSpan().Slice(_position, srcSpan.Length);
            srcSpan.CopyTo(dstSpan);
            _position += srcSpan.Length;
        }

        public bool Read<T>(ref int position, ref T value) where T : unmanaged
        {
            if (!_valid || position >= _position)
                return false;
            
            var valueSpan = MemoryMarshal.CreateSpan(ref value, 1);
            var dstSpan = MemoryMarshal.AsBytes(valueSpan);
            if (position + dstSpan.Length > _position)
            {
                _logger?.LogWarning("Tried to read past end of FrameRecorder buffer.");
                return false;
            }
            
            var srcSpan = BufferSpan().Slice(position, dstSpan.Length);
            srcSpan.CopyTo(dstSpan);
            position += dstSpan.Length;
            
            return true;
        }

        public void Reset()
        {
            _position = 0;
            _valid = true;
        }
        
        private unsafe Span<byte> BufferSpan()
        {
            return new Span<byte>(_buffer.ToPointer(), _bufferSize);
        }
    }
}

