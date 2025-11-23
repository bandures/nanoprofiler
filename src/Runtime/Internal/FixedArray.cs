using System;
using System.Collections;
using System.Collections.Generic;

namespace Meowtron.NanoProfiler
{
    internal class FixedArray<T> : IEnumerable<T>
    {
        private readonly T[] _array;
        private int _count;

        public FixedArray(int capacity)
        {
            _array = new T[capacity];
            _count = 0;
        }

        public ref T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                    throw new IndexOutOfRangeException();
                return ref _array[index];
            }
        }

        public int Count => _count;

        public int Capacity => _array.Length;

        public bool Add(T item)
        {
            if (_count >= _array.Length)
                return false;
            
            _array[_count] = item;
            _count++;
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < _count; i++)
                _array[i] = default;
            
            _count = 0;
        }

        public IEnumerator<T> GetEnumerator()
        {
            {
                for (int i = 0; i < _count; i++)
                {
                    yield return _array[i];
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }    
}