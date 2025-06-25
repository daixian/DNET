using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using DNET.Protocol;
using DNET;

namespace DNETUnitTest
{
    [TestClass]
    public class SimplePacketTest
    {
        [TestMethod]
        public void TestMethod_SimplePacket1()
        {
            var packet = new SimplePacket();

            // 构造测试数据
            byte[] testData = System.Text.Encoding.UTF8.GetBytes("Hello, SimplePacket!");
            var header = Header.CreateDefault();
            header.format = Format.None;
            header.txrId = 123;
            header.eventType = 456;
            header.dataLen = (uint)testData.Length;

            var msg = new Message {
                header = header,
                data = testData
            };

            // 测试 Pack
            ByteBuffer packedBuffer = packet.Pack(msg);

            Assert.IsNotNull(packedBuffer);
            Assert.IsTrue(packedBuffer.Length > 0);

            // 测试 Unpack
            List<Message> unpackedMessages = packet.Unpack(packedBuffer.buffer, packedBuffer.Length);
            Assert.IsNotNull(unpackedMessages);
            Assert.AreEqual(1, unpackedMessages.Count);

            var unpackedMsg = unpackedMessages[0];
            Assert.AreEqual(header.magic, unpackedMsg.header.magic);
            Assert.AreEqual(header.format, unpackedMsg.header.format);
            Assert.AreEqual(header.txrId, unpackedMsg.header.txrId);
            Assert.AreEqual(header.eventType, unpackedMsg.header.eventType);
            Assert.AreEqual(header.dataLen, unpackedMsg.header.dataLen);

            string unpackedString = System.Text.Encoding.UTF8.GetString(unpackedMsg.data);
            Assert.AreEqual("Hello, SimplePacket!", unpackedString);

            // 释放 ByteBuffer 资源（如果需要）
            packedBuffer.Recycle();
        }



        [TestMethod]
        public void TestMethod_SimplePacket_IncrementalUnpack()
        {
            var packet = new SimplePacket();

            // 构造测试数据
            byte[] testData = System.Text.Encoding.UTF8.GetBytes("Hello, SimplePacket!");
            var header = Header.CreateDefault();
            header.format = Format.None;
            header.txrId = 123;
            header.eventType = 456;
            header.dataLen = (uint)testData.Length;

            var msg = new Message {
                header = header,
                data = testData
            };

            // 先一次性Pack，得到完整数据包
            ByteBuffer packedBuffer = packet.Pack(msg);
            byte[] fullBuffer = packedBuffer.ToArray();

            List<Message> totalMessages = new List<Message>();

            // 模拟逐字节接收
            for (int i = 0; i < fullBuffer.Length; i++) {
                // 每次传入1字节
                byte[] oneByte = new byte[1] { fullBuffer[i] };

                // Unpack 返回可能的消息集合（可能空，因为数据不完整）
                var msgs = packet.Unpack(oneByte, 1);

                if (msgs != null && msgs.Count > 0) {
                    totalMessages.AddRange(msgs);
                }
            }

            // 断言最终收到了1条完整消息
            Assert.AreEqual(1, totalMessages.Count);

            var unpackedMsg = totalMessages[0];
            Assert.AreEqual(header.magic, unpackedMsg.header.magic);
            Assert.AreEqual(header.format, unpackedMsg.header.format);
            Assert.AreEqual(header.txrId, unpackedMsg.header.txrId);
            Assert.AreEqual(header.eventType, unpackedMsg.header.eventType);
            Assert.AreEqual(header.dataLen, unpackedMsg.header.dataLen);

            string unpackedString = System.Text.Encoding.UTF8.GetString(unpackedMsg.data);
            Assert.AreEqual("Hello, SimplePacket!", unpackedString);

            packedBuffer.Recycle();
        }

    }
}
