using System;
using System.Collections.Generic;

namespace RLGames
{
    /// <summary>
    /// Lightweight priority queue compatible with Unity/C# runtimes that may
    /// not include .NET 6's System.Collections.Generic.PriorityQueue.
    /// </summary>
    public class PriorityQueue<TElement, TPriority>
    {
        private readonly SortedSet<Entry> set;
        private long sequence;

        private struct Entry
        {
            public readonly TElement Element;
            public readonly TPriority Priority;
            public readonly long Sequence;

            public Entry(TElement element, TPriority priority, long sequence)
            {
                Element = element;
                Priority = priority;
                Sequence = sequence;
            }
        }

        private sealed class EntryComparer : IComparer<Entry>
        {
            private readonly IComparer<TPriority> priorityComparer;

            public EntryComparer(IComparer<TPriority> priorityComparer)
            {
                this.priorityComparer = priorityComparer ?? Comparer<TPriority>.Default;
            }

            public int Compare(Entry x, Entry y)
            {
                int priorityComparison = priorityComparer.Compare(x.Priority, y.Priority);
                if (priorityComparison != 0)
                    return priorityComparison;

                // Ensure strict ordering and allow duplicates with same priority.
                return x.Sequence.CompareTo(y.Sequence);
            }
        }

        public PriorityQueue()
            : this(null)
        {
        }

        public PriorityQueue(IComparer<TPriority> priorityComparer)
        {
            set = new SortedSet<Entry>(new EntryComparer(priorityComparer));
        }

        public int Count => set.Count;

        public void Enqueue(TElement element, TPriority priority)
        {
            set.Add(new Entry(element, priority, sequence));
            sequence++;
        }

        public TElement Dequeue()
        {
            if (set.Count == 0)
                throw new InvalidOperationException("PriorityQueue is empty.");

            Entry min = set.Min;
            set.Remove(min);
            return min.Element;
        }

        public void Clear()
        {
            set.Clear();
            sequence = 0;
        }
    }
}

