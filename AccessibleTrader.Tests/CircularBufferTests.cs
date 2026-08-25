using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Collections;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The <see cref="CircularBuffer{T}"/> eviction/wrap/prepend contract.
    ///
    /// <para>
    /// This file was DataCacheTests and also covered <c>DataCacheService</c>, the buffer's only
    /// consumer. That service was deleted on 2026-08-25 — it had no callers at all, and its
    /// <c>Add</c> corrupted its own lookup index on every eviction (surviving items all shift
    /// down one, but only the new entry's index was rewritten) while <c>AddRange</c> rebuilt
    /// the index correctly. The buffer itself is still used, so its tests stay.
    /// </para>
    /// </summary>
    public class CircularBufferTests
    {
        [Fact]
        public void CircularBuffer_ShouldEvictOldItems()
        {
            var buffer = new CircularBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            buffer.Add(4);

            Assert.Equal(3, buffer.Count);
            Assert.Equal(2, buffer[0]);
            Assert.Equal(3, buffer[1]);
            Assert.Equal(4, buffer[2]);
        }

        [Fact]
        public void CircularBuffer_GetLogicalIndex_ShouldMapCorrectlyAfterWrap()
        {
            var buffer = new CircularBuffer<int>(3);
            buffer.Add(1); // physical 0, logical 0
            buffer.Add(2); // physical 1, logical 1
            buffer.Add(3); // physical 2, logical 2
            buffer.Add(4); // physical 0 (wraps), logical 2. Head is physical 1.

            // Head is now at physical index 1 (value 2)
            // Logical index 0 should be physical 1
            // Logical index 1 should be physical 2
            // Logical index 2 should be physical 0
            Assert.Equal(0, buffer.GetLogicalIndex(1));
            Assert.Equal(1, buffer.GetLogicalIndex(2));
            Assert.Equal(2, buffer.GetLogicalIndex(0));
        }

        [Fact]
        public void CircularBuffer_Prepend_ShouldMaintainOrder()
        {
            var buffer = new CircularBuffer<int>(5);
            buffer.Add(3);
            buffer.Add(4);
            buffer.Prepend(2);
            buffer.Prepend(1);

            Assert.Equal(1, buffer[0]);
            Assert.Equal(2, buffer[1]);
            Assert.Equal(3, buffer[2]);
            Assert.Equal(4, buffer[3]);
        }

    }
}
