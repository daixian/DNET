using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using DNET;

namespace DNETUnitTest
{
    [TestClass]
    public class UnsafeByteBufferTest
    {
        [TestMethod]
        public void Append_And_ToArray_ShouldMatch()
        {
            var buffer = new UnsafeByteBuffer(8);
            byte[] data = { 1, 2, 3, 4 };
            buffer.Append(data, 0, data.Length);

            byte[] result = buffer.ToArray();
            CollectionAssert.AreEqual(data, result);
        }

        [TestMethod]
        public void Write_And_Read_Int_ShouldMatch()
        {
            var buffer = new UnsafeByteBuffer();
            int value = 12345678;
            buffer.Write(value);

            int read = buffer.Read<int>(0);
            Assert.AreEqual(value, read);
        }

        [TestMethod]
        public void Write_And_Read_Struct_ShouldMatch()
        {
            var buffer = new UnsafeByteBuffer();

            var original = new TestStruct { a = 42, b = 3.14f };
            buffer.Write(original);

            var read = buffer.Read<TestStruct>(0);
            Assert.AreEqual(original.a, read.a);
            Assert.AreEqual(original.b, read.b, 0.0001f);
        }

        [TestMethod]
        public void Erase_ShouldRemoveBytes()
        {
            var buffer = new UnsafeByteBuffer();
            byte[] data = { 10, 20, 30, 40, 50 };
            buffer.Append(data, 0, data.Length);

            buffer.Erase(1, 2); // 移除 20, 30

            byte[] expected = { 10, 40, 50 };
            byte[] result = buffer.ToArray();
            CollectionAssert.AreEqual(expected, result);
        }

        [TestMethod]
        public void EnsureCapacity_ShouldExpandBuffer()
        {
            var buffer = new UnsafeByteBuffer(4);
            byte[] data = new byte[100];
            for (int i = 0; i < 100; i++) data[i] = (byte)i;

            buffer.Append(data, 0, data.Length);
            byte[] result = buffer.ToArray();

            CollectionAssert.AreEqual(data, result);
            Assert.IsTrue(buffer.Capacity >= 100);
        }

        [TestMethod]
        public void Clear_ShouldResetPosition()
        {
            var buffer = new UnsafeByteBuffer();
            buffer.Write<int>(1234);
            buffer.Clear();
            Assert.AreEqual(0, buffer.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Read_BeyondPosition_ShouldThrow()
        {
            var buffer = new UnsafeByteBuffer();
            buffer.Write<int>(42);
            buffer.Read<int>(4); // 越界访问
        }


        [TestMethod]
        public void ToArray_ReturnsCorrectSubArray()
        {
            var buffer = new UnsafeByteBuffer();
            buffer.Append(new byte[] { 10, 20, 30, 40, 50 });

            var result = buffer.ToArray(1, 3); // should return [20,30,40]

            CollectionAssert.AreEqual(new byte[] { 20, 30, 40 }, result);
        }

        [TestMethod]
        public void ToArray_OffsetZero_ReturnsFullArray()
        {
            var buffer = new UnsafeByteBuffer();
            buffer.Append(new byte[] { 1, 2, 3, 4, 5 });

            var result = buffer.ToArray(0, buffer.Count);

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, result);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void ToArray_InvalidNegativeOffset_ThrowsException()
        {
            var buffer = new UnsafeByteBuffer();
            buffer.Append(new byte[] { 1, 2, 3 });
            buffer.ToArray(-1, 2);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void ToArray_CountExceedsValidRange_ThrowsException()
        {
            var buffer = new UnsafeByteBuffer();
            buffer.Append(new byte[] { 1, 2, 3 });
            buffer.ToArray(1, 5); // exceeds buffer.Count
        }

        [TestMethod]
        public void ToArray_ZeroLength_ReturnsEmptyArray()
        {
            var buffer = new UnsafeByteBuffer();
            buffer.Append(new byte[] { 1, 2, 3 }, 0, 3);
            var result = buffer.ToArray(2, 0); // should be empty
            Assert.AreEqual(0, result.Length);
        }

        private struct TestStruct
        {
            public int a;
            public float b;
        }
    }
}
