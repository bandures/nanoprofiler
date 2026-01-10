using System;
using System.Collections.Generic;

namespace NanoProfiler
{
    internal class PerfettoObjectPool<T> where T : new()
    {
        private readonly Stack<T> _pool;

        public PerfettoObjectPool(int initialSize)
        {
            _pool = new Stack<T>(initialSize);
            for (int i = 0; i < initialSize; i++)
            {
                _pool.Push(new T());
            }
        }

        public T Get()
        {
            if (_pool.Count > 0)
                return _pool.Pop();
            return new T();
        }

        public void Return(T item)
        {
            _pool.Push(item);
        }
    }
}
