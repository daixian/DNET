using System.Collections.Generic;
using NUnit.Framework;

namespace DNET.Test
{
    [TestFixture]
    public class SimplePacketTest
    {
        [Test]
        public unsafe void TestMethod_SimplePacket1()
        {
            SimplePacket packet = new SimplePacket();

            // 构造测试数据
            byte[] testData = System.Text.Encoding.UTF8.GetBytes("Hello, SimplePacket!");
            Header header = Header.CreateDefault();
            header.format = Format.None;
            header.txrId = 123;
            header.eventType = 456;
            header.dataLen = testData.Length;

            int headerSize = sizeof(Header);

            Message msg = new Message {
                header = header,
                data = new ByteBuffer(testData)
            };

            // 测试 Pack
            ByteBuffer packedBuffer = packet.Pack(msg);

            Assert.That(packedBuffer, Is.Not.Null);
            Assert.That(packedBuffer.Length, Is.GreaterThan(0));

            // 测试 Unpack
            var unpackedMessages = packet.Unpack(packedBuffer.Bytes, 0, packedBuffer.Length);
            Assert.That(unpackedMessages, Is.Not.Null);
            Assert.That(unpackedMessages.Count, Is.EqualTo(1));

            Message unpackedMsg = unpackedMessages[0];
            Assert.That(unpackedMsg.header.magic, Is.EqualTo(header.magic));
            Assert.That(unpackedMsg.header.format, Is.EqualTo(header.format));
            Assert.That(unpackedMsg.header.txrId, Is.EqualTo(header.txrId));
            Assert.That(unpackedMsg.header.eventType, Is.EqualTo(header.eventType));
            Assert.That(unpackedMsg.header.dataLen, Is.EqualTo(header.dataLen));

            string unpackedString = System.Text.Encoding.UTF8.GetString(unpackedMsg.data.Bytes, 0, unpackedMsg.data.Length);
            Assert.That(unpackedString, Is.EqualTo("Hello, SimplePacket!"));

            // 释放 ByteBuffer 资源（如果需要）
            packedBuffer.Recycle();
        }


        [Test]
        public void TestMethod_SimplePacket_IncrementalUnpack()
        {
            SimplePacket packet = new SimplePacket();

            // 构造测试数据
            byte[] testData = System.Text.Encoding.UTF8.GetBytes("Hello, SimplePacket!");
            Header header = Header.CreateDefault();
            header.format = Format.None;
            header.txrId = 123;
            header.eventType = 456;
            header.dataLen = testData.Length;

            Message msg = new Message {
                header = header,
                data = new ByteBuffer(testData)
            };

            // 先一次性Pack，得到完整数据包
            ByteBuffer packedBuffer = packet.Pack(msg);
            byte[] fullBuffer = packedBuffer.ToArray();

            var totalMessages = new List<Message>();

            // 模拟逐字节接收
            for (int i = 0; i < fullBuffer.Length; i++) {
                // 每次传入1字节
                byte[] oneByte = new byte[1] { fullBuffer[i] };

                // Unpack 返回可能的消息集合（可能空，因为数据不完整）
                var msgs = packet.Unpack(oneByte, 0, 1);

                if (msgs != null && msgs.Count > 0) totalMessages.AddRange(msgs);
            }

            // 断言最终收到了1条完整消息
            Assert.That(totalMessages.Count, Is.EqualTo(1));

            Message unpackedMsg = totalMessages[0];
            Assert.That(unpackedMsg.header.magic, Is.EqualTo(header.magic));
            Assert.That(unpackedMsg.header.format, Is.EqualTo(header.format));
            Assert.That(unpackedMsg.header.txrId, Is.EqualTo(header.txrId));
            Assert.That(unpackedMsg.header.eventType, Is.EqualTo(header.eventType));
            Assert.That(unpackedMsg.header.dataLen, Is.EqualTo(header.dataLen));

            string unpackedString = System.Text.Encoding.UTF8.GetString(unpackedMsg.data.Bytes, 0, unpackedMsg.data.Length);
            Assert.That(unpackedString, Is.EqualTo("Hello, SimplePacket!"));

            packedBuffer.Recycle();
        }
    }
}
