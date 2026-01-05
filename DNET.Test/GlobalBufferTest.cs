using System.Text;
using NUnit.Framework;

namespace DNET.Test
{
    [TestFixture]
    public class GlobalBufferTest
    {
        [Test]
        public void GetEncodedUtf8_AsciiString()
        {
            string text = "hello world";

            var buffer = GlobalBuffer.Inst.GetEncodedUtf8(text);

            Assert.NotNull(buffer);
            Assert.GreaterOrEqual(buffer.Capacity, buffer.Length);

            string decoded = Encoding.UTF8.GetString(buffer.Bytes, 0, buffer.Length);
            Assert.That(decoded, Is.EqualTo(text));
            buffer.Recycle();
        }

        [Test]
        public void GetEncodedUtf8_ChineseString()
        {
            string text = "你好世界";

            var buffer = GlobalBuffer.Inst.GetEncodedUtf8(text);

            string decoded = Encoding.UTF8.GetString(buffer.Bytes, 0, buffer.Length);
            Assert.That(decoded, Is.EqualTo(text));
            buffer.Recycle();
        }

        [Test]
        public void GetEncodedUtf8_EmojiString()
        {
            string text = "😄🚀🌍";

            var buffer = GlobalBuffer.Inst.GetEncodedUtf8(text);

            string decoded = Encoding.UTF8.GetString(buffer.Bytes, 0, buffer.Length);
            Assert.That(decoded, Is.EqualTo(text));
            buffer.Recycle();
        }

        [Test]
        public void GetEncodedUtf8_EmptyString()
        {
            string text = string.Empty;

            var buffer = GlobalBuffer.Inst.GetEncodedUtf8(text);

            Assert.NotNull(buffer);
            Assert.That(buffer.Length, Is.EqualTo(0));
            buffer.Recycle();
        }

        [Test]
        public void GetEncodedUtf8_BufferLengthIsExact()
        {
            string text = "hello你好";

            var buffer = GlobalBuffer.Inst.GetEncodedUtf8(text);

            int expectedBytes = Encoding.UTF8.GetByteCount(text);
            Assert.That(buffer.Length, Is.EqualTo(expectedBytes));
            buffer.Recycle();
        }

        [Test]
        public void GetEncodedUtf8_MultipleCalls_NoException()
        {
            for (int i = 0; i < 1000; i++) {
                string text = "msg_" + i;

                var buffer = GlobalBuffer.Inst.GetEncodedUtf8(text);
                string decoded = Encoding.UTF8.GetString(buffer.Bytes, 0, buffer.Length);

                Assert.That(decoded, Is.EqualTo(text));
                buffer.Recycle();
            }
        }
    }
}
