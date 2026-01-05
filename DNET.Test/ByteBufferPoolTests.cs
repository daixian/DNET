using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DNET.Test
{
    [TestFixture]
    public class ByteBufferPoolTests
    {
        [Test]
        public void MultiThread_GetAndRecycle_ShouldBeThreadSafe()
        {
            int threadCount = 16;
            int iterationsPerThread = 100_000;
            int blockSize = 256;
            int capacityLimit = 128;

            ByteBufferPool pool = new ByteBufferPool(blockSize, capacityLimit);

            // 用于记录是否出异常
            var exceptions = new ConcurrentQueue<Exception>();

            Parallel.For(0, threadCount, t => {
                try {
                    for (int i = 0; i < iterationsPerThread; i++) {
                        // 模拟不同大小的请求
                        int size = i % 4 == 0 ? blockSize * 2 : blockSize;

                        ByteBuffer buf = pool.Get(size);

                        Assert.That(buf.Length, Is.EqualTo(0)); // 重点检查这里
                        Assert.That(buf.Capacity, Is.GreaterThanOrEqualTo(size));

                        // 模拟写入
                        buf.Write(new byte[16], 0, 16);
                        Assert.That(buf.Length, Is.EqualTo(16)); // 重点检查这里

                        // 模拟使用后归还
                        pool.Recycle(buf);
                    }
                } catch (Exception ex) {
                    exceptions.Enqueue(ex);
                }
            });

            // 所有线程完成后检查
            Assert.That(exceptions, Is.Empty, $"有异常发生: {string.Join("\n", exceptions)}");

            // 检查分配数量是否合理 LogProxy.LogDebug
            Console.WriteLine($"池中剩余数量: {pool.InPoolCount}, 总共分配: {pool.TotalAllocated},成功复用次数: {pool.ReusedCount}");

            Assert.That(pool.InPoolCount, Is.LessThanOrEqualTo(capacityLimit), "池中数量不能超过上限");
            Assert.That(pool.TotalAllocated, Is.GreaterThan(0), "应至少分配过一次");
        }
    }
}
