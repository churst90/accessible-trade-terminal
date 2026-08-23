using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Collections;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Services;
using Xunit;

namespace AccessibleTrader.Tests
{
    public class DataCacheTests
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

        [Fact]
        public void DataCacheService_ShouldUpdateExistingTick()
        {
            var cache = new DataCacheService();
            var date = DateTime.UtcNow;
            var tick1 = new Ohlcv { Date = date, Close = 100 };
            var tick2 = new Ohlcv { Date = date, Close = 105 };

            cache.Add(tick1);
            cache.Add(tick2);

            Assert.Equal(1, cache.Count);
            Assert.Equal(105, (double)cache.Data[0].Close);
        }

        [Fact]
        public void DataCacheService_ShouldMaintainCapacity()
        {
            var cache = new DataCacheService();
            for (int i = 0; i < 6000; i++)
            {
                cache.Add(new Ohlcv { Date = DateTime.UtcNow.AddMinutes(i), Close = i });
            }

            Assert.Equal(5000, cache.Count);
        }
    }
}



