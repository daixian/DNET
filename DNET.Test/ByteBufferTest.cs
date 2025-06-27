using System;
using NUnit.Framework;

namespace DNET.Test
{
    [TestFixture]
    public class ByteBufferTest
    {
        [Test]
        public void Test_WriteAndToArray_ShouldMatch()
        {
            ByteBuffer buffer = new ByteBuffer(64);
            byte[] data = { 1, 2, 3, 4, 5 };
            buffer.Write(data, 0, data.Length);

            byte[] result = buffer.ToArray();
            Assert.That(result.Length, Is.EqualTo(data.Length));
            Assert.That(result, Is.EqualTo(data));
        }

        [Test]
        public void Test_Reset_ShouldClearLength()
        {
            ByteBuffer buffer = new ByteBuffer(32);
            buffer.Write(new byte[] { 1, 2, 3 }, 0, 3);

            Assert.That(buffer.Length, Is.EqualTo(3));

            buffer.Reset();
            Assert.That(buffer.Length, Is.EqualTo(0));
        }

        [Test]
        public void Test_Append_ShouldIncreaseLength()
        {
            ByteBuffer buf1 = new ByteBuffer(32);
            ByteBuffer buf2 = new ByteBuffer(32);

            buf1.Write(new byte[] { 1, 2 }, 0, 2);
            buf2.Write(new byte[] { 3, 4, 5 }, 0, 3);

            buf1.Append(buf2);

            byte[] result = buf1.ToArray();
            Assert.That(result, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
            Assert.That(buf1.Length, Is.EqualTo(5));
        }

        [Test]
        public void Test_Append_ByteArray()
        {
            ByteBuffer buffer = new ByteBuffer(16);
            buffer.Append(new byte[] { 10, 20 }, 0, 2);
            buffer.Append(new byte[] { 30, 40 }, 0, 2);

            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer.ToArray(), Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
        }

        [Test]
        public void Test_Write_Overflow_ShouldThrow()
        {
            ByteBuffer buffer = new ByteBuffer(4);
            Assert.Throws<InvalidOperationException>(() => { buffer.Write(new byte[] { 1, 2, 3, 4, 5 }, 0, 5); });
        }

        [Test]
        public void Test_Append_Overflow_ShouldThrow()
        {
            ByteBuffer buffer = new ByteBuffer(4);
            buffer.Write(new byte[] { 1, 2 }, 0, 2);

            Assert.Throws<InvalidOperationException>(() => { buffer.Append(new byte[] { 3, 4, 5 }, 0, 3); });
        }

        [Test]
        public unsafe void Test_WriteUnsafe_ShouldWriteCorrectly()
        {
            ByteBuffer buffer = new ByteBuffer(8);
            byte[] source = { 100, 101, 102, 103 };

            fixed (byte* p = source) {
                buffer.Write(p, 4);
            }

            Assert.That(buffer.Length, Is.EqualTo(4));
            Assert.That(buffer.ToArray(), Is.EqualTo(source));
        }
    }
}
