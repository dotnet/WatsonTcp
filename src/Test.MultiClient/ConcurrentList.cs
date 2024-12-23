namespace ConcurrentList
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;

    public sealed class ConcurrentList<T> : ThreadSafeList<T>
    {
        private static readonly int[] Sizes;
        private static readonly int[] Counts;

        static ConcurrentList()
        {
            Sizes = new int[32];
            Counts = new int[32];

            int size = 1;
            int count = 1;
            for (int i = 0; i < Sizes.Length; i++)
            {
                Sizes[i] = size;
                Counts[i] = count;

                if (i < Sizes.Length - 1)
                {
                    size *= 2;
                    count += size;
                }
            }
        }

        private int _index;
        private int _fuzzyCount;
        private int _count;
        private T[][] _array;

        public ConcurrentList()
        {
            _array = new T[32][];
        }

        public override T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                int arrayIndex = GetArrayIndex(index + 1);
                if (arrayIndex > 0)
                {
                    index -= ((int)Math.Pow(2, arrayIndex) - 1);
                }

                return _array[arrayIndex][index];
            }
        }

        public override int Count
        {
            get
            {
                return _count;
            }
        }

        public override void Add(T element)
        {
            int index = Interlocked.Increment(ref _index) - 1;
            int adjustedIndex = index;

            int arrayIndex = GetArrayIndex(index + 1);
            if (arrayIndex > 0)
            {
                adjustedIndex -= Counts[arrayIndex - 1];
            }

            if (_array[arrayIndex] == null)
            {
                int arrayLength = Sizes[arrayIndex];
                Interlocked.CompareExchange(ref _array[arrayIndex], new T[arrayLength], null);
            }

            _array[arrayIndex][adjustedIndex] = element;

            int count = _count;
            int fuzzyCount = Interlocked.Increment(ref _fuzzyCount);
            if (fuzzyCount == index + 1)
            {
                Interlocked.CompareExchange(ref _count, fuzzyCount, count);
            }
        }

        public override void CopyTo(T[] array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            int count = _count;
            if (array.Length - index < count)
            {
                throw new ArgumentException("There is not enough available space in the destination array.");
            }

            int arrayIndex = 0;
            int elementsRemaining = count;
            while (elementsRemaining > 0)
            {
                T[] source = _array[arrayIndex++];
                int elementsToCopy = Math.Min(source.Length, elementsRemaining);
                int startIndex = count - elementsRemaining;

                Array.Copy(source, 0, array, startIndex, elementsToCopy);

                elementsRemaining -= elementsToCopy;
            }
        }

        private static int GetArrayIndex(int count)
        {
            int arrayIndex = 0;

            if ((count & 0xFFFF0000) != 0)
            {
                count >>= 16;
                arrayIndex |= 16;
            }

            if ((count & 0xFF00) != 0)
            {
                count >>= 8;
                arrayIndex |= 8;
            }

            if ((count & 0xF0) != 0)
            {
                count >>= 4;
                arrayIndex |= 4;
            }

            if ((count & 0xC) != 0)
            {
                count >>= 2;
                arrayIndex |= 2;
            }

            if ((count & 0x2) != 0)
            {
                count >>= 1;
                arrayIndex |= 1;
            }

            return arrayIndex;
        }

        #region "Protected methods"

        protected override bool IsSynchronizedBase
        {
            get { return false; }
        }

        #endregion "Protected methods"
    }

    public abstract class ThreadSafeList<T> : IList<T>, IList
    {
        public abstract T this[int index] { get; }

        public abstract int Count { get; }

        public abstract void Add(T item);

        public virtual int IndexOf(T item)
        {
            IEqualityComparer<T> comparer = EqualityComparer<T>.Default;

            int count = Count;
            for (int i = 0; i < count; i++)
            {
                if (comparer.Equals(item, this[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        public virtual bool Contains(T item)
        {
            return IndexOf(item) != -1;
        }

        public abstract void CopyTo(T[] array, int arrayIndex);

        public IEnumerator<T> GetEnumerator()
        {
            int count = Count;
            for (int i = 0; i < count; i++)
            {
                yield return this[i];
            }
        }

        #region "Protected methods"

        protected abstract bool IsSynchronizedBase { get; }

        protected virtual void CopyToBase(Array array, int arrayIndex)
        {
            for (int i = 0; i < this.Count; ++i)
            {
                array.SetValue(this[i], arrayIndex + i);
            }
        }

        protected virtual int AddBase(object value)
        {
            Add((T)value);
            return Count - 1;
        }

        #endregion "Protected methods"

        #region "Explicit interface implementations"

        T IList<T>.this[int index]
        {
            get { return this[index]; }
            set { throw new NotSupportedException(); }
        }

        void IList<T>.Insert(int index, T item)
        {
            throw new NotSupportedException();
        }

        void IList<T>.RemoveAt(int index)
        {
            throw new NotSupportedException();
        }

        bool ICollection<T>.IsReadOnly
        {
            get { return false; }
        }

        void ICollection<T>.Clear()
        {
            throw new NotSupportedException();
        }

        bool ICollection<T>.Remove(T item)
        {
            throw new NotSupportedException();
        }

        bool IList.IsFixedSize
        {
            get { return false; }
        }

        bool IList.IsReadOnly
        {
            get { return false; }
        }

        object IList.this[int index]
        {
            get { return this[index]; }
            set { throw new NotSupportedException(); }
        }

        void IList.RemoveAt(int index)
        {
            throw new NotSupportedException();
        }

        void IList.Remove(object value)
        {
            throw new NotSupportedException();
        }

        void IList.Insert(int index, object value)
        {
            throw new NotSupportedException();
        }

        int IList.IndexOf(object value)
        {
            return IndexOf((T)value);
        }

        void IList.Clear()
        {
            throw new NotSupportedException();
        }

        bool IList.Contains(object value)
        {
            return ((IList)this).IndexOf(value) != -1;
        }

        int IList.Add(object value)
        {
            return AddBase(value);
        }

        bool ICollection.IsSynchronized
        {
            get { return IsSynchronizedBase; }
        }

        object ICollection.SyncRoot
        {
            get { return null; }
        }

        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            CopyToBase(array, arrayIndex);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion "Explicit interface implementations"
    }
}