using System;
using NUnit.Framework;

namespace DNET.Test
{
    [TestFixture]
    public class UnsafeByteBufferTest
    {
        [Test]
        public void Append_And_ToArray_ShouldMatch()
        {
            UnsafeByteBuffer buffer = new UnsafeByteBuffer(8);
            byte[] data = { 1, 2, 3, 4 };
            buffer.Append(data, 0, data.Length);

            byte[] result = buffer.ToArray();
            Assert.That(result, Is.EqualTo(data));
        }

        [Test]
        public void Write_And_Read_Int_ShouldMatch()
        {
            UnsafeByteBuffer buffer = new UnsafeByteBuffer();
            int value = 12345678;
            buffer.Write(value);

            int read = buffer.Read<int>();
            Assert.That(read, Is.EqualTo(value));
        }

        [Test]
        public void Write_And_Read_Struct_ShouldMatch()
        {
            UnsafeByteBuffer buffer = new UnsafeByteBuffer();

            TestStruct original = new TestStruct { a = 42, b = 3.14f };
            buffer.Write(original);

            TestStruct read = buffer.Read<TestStruct>();
            Assert.That(read.a, Is.EqualTo(original.a));
            Assert.That(read.b, Is.EqualTo(original.b).Within(0.0001f));
        }

        [Test]
        public void Erase_ShouldRemoveBytes()
        {
            UnsafeByteBuffer buffer = new UnsafeByteBuffer();
            byte[] data = { 10, 20, 30, 40, 50 };
            buffer.Append(data, 0, data.Length);

            buffer.Erase(1, 2); // 移除 20, 30

            byte[] expected = { 10, 40, 50 };
            byte[] result = buffer.ToArray();
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void EnsureCapacity_ShouldExpandBuffer()
        {
            UnsafeByteBuffer buffer = new UnsafeByteBuffer(4);
            byte[] data = new byte[100];
            for (int i = 0; i < 100; i++) data[i] = (byte)i;

            buffer.Append(data, 0, data.Length);
            byte[] result = buffer.ToArray();

            Assert.That(result, Is.EqualTo(data));
            Assert.That(buffer.Capacity, Is.GreaterThanOrEqualTo(100));
        }

        [Test]
        public void Clear_ShouldResetPosition()
        {
            UnsafeByteBuffer buffer = new UnsafeByteBuffer();
            buffer.Write(1234);
            buffer.Clear();
            Assert.That(buffer.Count, Is.EqualTo(0));
        }

        [Test]
        public void Read_BeyondPosition_ShouldThrow()
        {
            UnsafeByteBuffer buffer = new UnsafeByteBuffer();
            buffer.Write(42);

            Assert.Throws<InvalidOperationException>(() => {
                buffer.Read<int>(4); // 越界访问
            });
        }

        [Test]
        public void ToArray_ReturnsCorrectSubArray()
        {
            UnsafeByteBuffer buffer = new UnsafeByteBuffer();
            buffer.Append(new byte[] { 10, 20, 30, 40, 50 });

            byte[] result = buffer.ToArray(1, 3); // should return [20,30,40]
            Assert.That(result, Is.EqualTo(new byte[] { 20, 30, 40 }));
        }

        [Test]
        public void ToArray_OffsetZero_ReturnsFullArray()
        {
            UnsafeByteBuffer buffer = new UnsafeByteBuffer();
            buffer.Append(new byte[] { 1, 2, 3, 4, 5 });

            byte[] result = buffer.ToArray(0, buffer.Count);
            Assert.That(result, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        }

        [Test]
        public void ToArray_InvalidNegativeOffset_ThrowsException()
        {
            UnsafeByteBuffer buffer = new UnsafeByteBuffer();
            buffer.Append(new byte[] { 1, 2, 3 });

            Assert.Throws<ArgumentOutOfRangeException>(() => { buffer.ToArray(-1, 2); });
        }

        [Test]
        public void ToArray_CountExceedsValidRange_ThrowsException()
        {
            UnsafeByteBuffer buffer = new UnsafeByteBuffer();
            buffer.Append(new byte[] { 1, 2, 3 });

            Assert.Throws<ArgumentOutOfRangeException>(() => { buffer.ToArray(1, 5); });
        }

        [Test]
        public void ToArray_ZeroLength_ReturnsEmptyArray()
        {
            UnsafeByteBuffer buffer = new UnsafeByteBuffer();
            buffer.Append(new byte[] { 1, 2, 3 }, 0, 3);
            byte[] result = buffer.ToArray(2, 0);
            Assert.That(result.Length, Is.EqualTo(0));
        }

        private struct TestStruct
        {
            public int a;
            public float b;
        }
    }
}
