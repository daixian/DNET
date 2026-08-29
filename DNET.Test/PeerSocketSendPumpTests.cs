using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DNET.Test
{
    /// <summary>
    /// 直接验证PeerSocket发送泵在背压、并发生产、慢速接收和Socket配置场景下的关键不变量。
    /// 测试使用系统分配的回环临时端口和原始Socket接收端，不依赖DNClient、DNServer单例、
    /// 固定端口或定时器，因此失败时可以直接归因到单连接发送路径。
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class PeerSocketSendPumpTests
    {
        /// <summary>
        /// 单项异步操作允许等待的最长时间，避免网络异常时测试无限阻塞。
        /// </summary>
        private const int OPERATION_TIMEOUT_MILLISECONDS = 20000;

        /// <summary>
        /// DNET协议头使用的固定魔数。
        /// </summary>
        private const uint MESSAGE_MAGIC = 0x584D5347;

        #region 参数与背压测试

        /// <summary>
        /// 验证单Peer达到待发送字节上限后会拒绝新消息，并且Clear能够完整释放已预留配额。
        /// </summary>
        [Test]
        public void TryAddSendData_WhenPendingLimitExceeded_ShouldRejectAndReleaseReservation()
        {
            long originalLimit = Config.MaxPendingSendBytesPerPeer;
            var peerSocket = new PeerSocket { Name = nameof(TryAddSendData_WhenPendingLimitExceeded_ShouldRejectAndReleaseReservation) };
            try {
                byte[] payload = { 1, 2, 3, 4 };
                long packetLength = Message.HeaderLength + payload.Length;
                Config.MaxPendingSendBytesPerPeer = packetLength;

                bool firstAccepted = peerSocket.TryAddSendData(payload, 0, payload.Length);
                bool secondAccepted = peerSocket.TryAddSendData(payload, 0, payload.Length);

                Assert.That(firstAccepted, Is.True, "上限内的第一条消息不应被拒绝");
                Assert.That(secondAccepted, Is.False, "超过单Peer待发送字节上限的消息应被拒绝");
                Assert.That(peerSocket.WaitSendMsgCount, Is.EqualTo(1), "拒绝新消息不应污染发送队列");
                Assert.That(peerSocket.PendingSendBytes, Is.EqualTo(packetLength), "待发送字节应只包含成功入队的消息");

                peerSocket.Clear();

                Assert.That(peerSocket.WaitSendMsgCount, Is.Zero, "Clear后发送队列必须为空");
                Assert.That(peerSocket.PendingSendBytes, Is.Zero, "Clear后背压配额必须全部归还");
            } finally {
                Config.MaxPendingSendBytesPerPeer = originalLimit;
                peerSocket.Dispose();
            }
        }

        /// <summary>
        /// 验证无效数据范围不会进入发送队列，也不会遗留待发送字节配额。
        /// </summary>
        [Test]
        public void TryAddSendData_WhenDataRangeInvalid_ShouldNotLeakPendingBytes()
        {
            var peerSocket = new PeerSocket { Name = nameof(TryAddSendData_WhenDataRangeInvalid_ShouldNotLeakPendingBytes) };
            try {
                byte[] payload = { 1, 2, 3, 4 };

                bool negativeOffsetAccepted = peerSocket.TryAddSendData(payload, -1, 1);
                bool overflowingRangeAccepted = peerSocket.TryAddSendData(payload, 2, 3);
                bool negativeLengthAccepted = peerSocket.TryAddSendData(payload, 0, -1);

                Assert.That(negativeOffsetAccepted, Is.False, "负偏移必须被拒绝");
                Assert.That(overflowingRangeAccepted, Is.False, "超出数组范围的数据必须被拒绝");
                Assert.That(negativeLengthAccepted, Is.False, "负长度必须被拒绝");
                Assert.That(peerSocket.WaitSendMsgCount, Is.Zero, "无效参数不应生成待发送消息");
                Assert.That(peerSocket.PendingSendBytes, Is.Zero, "无效参数不应占用背压配额");
            } finally {
                peerSocket.Dispose();
            }
        }

        #endregion

        #region Socket配置测试

        /// <summary>
        /// 验证客户端连接Socket会应用全局TCP_NODELAY配置，而不是只保留在配置对象中。
        /// </summary>
        /// <param name="enabled">期望写入Socket的TCP_NODELAY配置。</param>
        [TestCase(true)]
        [TestCase(false)]
        public void BindRemote_ShouldApplyConfiguredTcpNoDelay(bool enabled)
        {
            bool originalTcpNoDelay = Config.EnableTcpNoDelay;
            try {
                Config.EnableTcpNoDelay = enabled;
                using (LoopbackPeerSocketPair pair = LoopbackPeerSocketPair.Create()) {
                    Assert.That(pair.Sender.socket.NoDelay, Is.EqualTo(enabled), "连接Socket未应用Config.EnableTcpNoDelay");
                }
            } finally {
                Config.EnableTcpNoDelay = originalTcpNoDelay;
            }
        }

        #endregion

        #region 并发发送测试

        /// <summary>
        /// 验证多个生产线程同时向一个Peer入队和抢占发送权时，每条消息只发送一次，
        /// 且发送泵在线路上写入的消息ID严格连续，不发生丢失唤醒或重复提交。
        /// </summary>
        [Test]
        [Repeat(10)]
        [Timeout(30000)]
        public void ConcurrentProducers_ShouldDeliverEveryMessageExactlyOnceAndInWireIdOrder()
        {
            const int producerCount = 8;
            const int messagesPerProducer = 400;
            int expectedMessageCount = producerCount * messagesPerProducer;

            using (LoopbackPeerSocketPair pair = LoopbackPeerSocketPair.Create())
            using (var startGate = new ManualResetEventSlim(false)) {
                Task<List<ReceivedPacket>> receiveTask = Task.Run(
                    () => ReceivePackets(pair.Receiver, expectedMessageCount, 1024));
                Task[] producers = new Task[producerCount];

                for (int producerIndex = 0; producerIndex < producerCount; producerIndex++) {
                    int capturedProducerIndex = producerIndex;
                    producers[producerIndex] = Task.Run(() => {
                        startGate.Wait();
                        for (int sequence = 0; sequence < messagesPerProducer; sequence++) {
                            byte[] payload = CreateProducerPayload(capturedProducerIndex, sequence);
                            if (!pair.Sender.TryAddSendData(payload, 0, payload.Length)) {
                                throw new InvalidOperationException(
                                    $"生产者{capturedProducerIndex}的第{sequence}条消息被意外拒绝");
                            }
                            pair.Sender.TryStartSend();
                        }
                    });
                }

                startGate.Set();
                bool producersCompleted = Task.WaitAll(producers, OPERATION_TIMEOUT_MILLISECONDS);
                Assert.That(producersCompleted, Is.True, "并发生产线程未在限定时间内完成");

                List<ReceivedPacket> receivedPackets = WaitForResult(receiveTask, "接收端未收到全部并发消息");
                AssertConcurrentMessages(receivedPackets, producerCount, messagesPerProducer);
                Assert.That(
                    WaitUntil(() => pair.Sender.PendingSendBytes == 0, OPERATION_TIMEOUT_MILLISECONDS),
                    Is.True,
                    "接收完成后发送泵仍未归还全部待发送字节");
                Assert.That(pair.Sender.WaitSendMsgCount, Is.Zero, "接收完成后发送队列必须为空");
                Assert.That(pair.Sender.Status.SendMessageCount, Is.EqualTo(expectedMessageCount), "发送统计消息数不正确");
            }
        }

        #endregion

        #region 部分发送与完整性测试

        /// <summary>
        /// 验证小Socket缓冲区和小块慢读取条件下，大消息经过多次发送完成后仍保持完整，
        /// 并且发送统计和待发送字节计数只在整个协议包完成后结算。
        /// </summary>
        [Test]
        [Timeout(30000)]
        public void LargePayload_WithSmallSocketBuffer_ShouldArriveWithoutTruncation()
        {
            const int payloadLength = 2 * 1024 * 1024;
            byte[] payload = CreateDeterministicPayload(payloadLength);

            using (LoopbackPeerSocketPair pair = LoopbackPeerSocketPair.Create()) {
                pair.Sender.socket.SendBufferSize = 1024;
                pair.Receiver.ReceiveBufferSize = 1024;
                Task<List<ReceivedPacket>> receiveTask = Task.Run(() => ReceivePackets(pair.Receiver, 1, 257));

                Assert.That(pair.Sender.TryAddSendData(payload, 0, payload.Length), Is.True, "大消息不应被默认背压上限拒绝");
                Assert.That(pair.Sender.TryStartSend(), Is.True, "发送泵应成功提交大消息");

                List<ReceivedPacket> receivedPackets = WaitForResult(receiveTask, "接收端未完整收到大消息");
                Assert.That(receivedPackets.Count, Is.EqualTo(1));
                Assert.That(receivedPackets[0].header.id, Is.Zero, "首条消息ID应为0");
                Assert.That(receivedPackets[0].header.dataLen, Is.EqualTo(payload.Length), "协议头数据长度不正确");
                AssertPayloadEquals(payload, receivedPackets[0].payload);
                Assert.That(
                    WaitUntil(() => pair.Sender.PendingSendBytes == 0, OPERATION_TIMEOUT_MILLISECONDS),
                    Is.True,
                    "大消息完成后待发送字节未归零");
                Assert.That(pair.Sender.WaitSendMsgCount, Is.Zero, "大消息完成后发送队列必须为空");
                Assert.That(pair.Sender.Status.SendMessageCount, Is.EqualTo(1), "大消息只应统计为一条消息");
                Assert.That(
                    pair.Sender.Status.SendBytesCount,
                    Is.EqualTo(Message.HeaderLength + payload.Length),
                    "发送字节统计必须包含协议头且不能重复累计");
            }
        }

        #endregion

        #region 测试辅助

        /// <summary>
        /// 创建可唯一标识生产者和生产序号的八字节负载。
        /// </summary>
        /// <param name="producerIndex">生产者序号。</param>
        /// <param name="sequence">生产者内部消息序号。</param>
        /// <returns>包含两个Int32值的固定长度负载。</returns>
        private static byte[] CreateProducerPayload(int producerIndex, int sequence)
        {
            byte[] payload = new byte[sizeof(int) * 2];
            Buffer.BlockCopy(BitConverter.GetBytes(producerIndex), 0, payload, 0, sizeof(int));
            Buffer.BlockCopy(BitConverter.GetBytes(sequence), 0, payload, sizeof(int), sizeof(int));
            return payload;
        }

        /// <summary>
        /// 创建内容可复现的大负载，避免随机数使失败样本不可重放。
        /// </summary>
        /// <param name="length">负载字节数。</param>
        /// <returns>按固定公式生成的字节数组。</returns>
        private static byte[] CreateDeterministicPayload(int length)
        {
            byte[] payload = new byte[length];
            for (int i = 0; i < payload.Length; i++) {
                payload[i] = (byte)((i * 31 + 17) & 0xFF);
            }
            return payload;
        }

        /// <summary>
        /// 从原始Socket按DNET协议精确读取指定数量的消息。
        /// </summary>
        /// <param name="socket">原始接收Socket。</param>
        /// <param name="messageCount">预期消息数量。</param>
        /// <param name="maximumReceiveBytes">单次Receive允许读取的最大字节数。</param>
        /// <returns>按线路顺序接收到的消息。</returns>
        private static List<ReceivedPacket> ReceivePackets(Socket socket, int messageCount, int maximumReceiveBytes)
        {
            var packets = new List<ReceivedPacket>(messageCount);
            for (int i = 0; i < messageCount; i++) {
                byte[] headerBytes = ReceiveExact(socket, Message.HeaderLength, maximumReceiveBytes);
                Header header = headerBytes.GetHeader();
                if (header.magic != MESSAGE_MAGIC) {
                    throw new InvalidDataException($"第{i}条消息魔数错误: 0x{header.magic:X8}");
                }
                if (header.dataLen < 0 || header.dataLen > 16 * 1024 * 1024) {
                    throw new InvalidDataException($"第{i}条消息长度非法: {header.dataLen}");
                }

                packets.Add(new ReceivedPacket {
                    header = header,
                    payload = ReceiveExact(socket, header.dataLen, maximumReceiveBytes)
                });
            }
            return packets;
        }

        /// <summary>
        /// 循环读取直到得到指定字节数，主动处理TCP分片和短读取。
        /// </summary>
        /// <param name="socket">接收Socket。</param>
        /// <param name="length">需要读取的总字节数。</param>
        /// <param name="maximumReceiveBytes">单次Receive最大读取量。</param>
        /// <returns>长度严格等于请求值的字节数组。</returns>
        private static byte[] ReceiveExact(Socket socket, int length, int maximumReceiveBytes)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length) {
                int receiveLength = Math.Min(length - offset, maximumReceiveBytes);
                int received = socket.Receive(buffer, offset, receiveLength, SocketFlags.None);
                if (received == 0) {
                    throw new EndOfStreamException($"TCP连接提前关闭，期望{length}字节，实际只收到{offset}字节");
                }
                offset += received;
            }
            return buffer;
        }

        /// <summary>
        /// 等待异步接收任务完成并返回结果。
        /// </summary>
        /// <typeparam name="T">任务结果类型。</typeparam>
        /// <param name="task">要等待的任务。</param>
        /// <param name="timeoutMessage">超时时的断言信息。</param>
        /// <returns>任务成功完成后的结果。</returns>
        private static T WaitForResult<T>(Task<T> task, string timeoutMessage)
        {
            bool completed = task.Wait(OPERATION_TIMEOUT_MILLISECONDS);
            Assert.That(completed, Is.True, timeoutMessage);
            return task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// 在限定时间内等待一个无副作用条件成立，避免使用固定Sleep猜测异步完成时刻。
        /// </summary>
        /// <param name="condition">要观察的完成条件。</param>
        /// <param name="timeoutMilliseconds">最长等待毫秒数。</param>
        /// <returns>条件在超时前成立时返回true。</returns>
        private static bool WaitUntil(Func<bool> condition, int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            var spinWait = new SpinWait();
            while (DateTime.UtcNow < deadline) {
                if (condition()) {
                    return true;
                }
                spinWait.SpinOnce();
            }
            return condition();
        }

        /// <summary>
        /// 断言并发接收结果的线路ID连续，并且每个生产者的每个序号恰好出现一次。
        /// </summary>
        /// <param name="packets">线路顺序接收到的消息。</param>
        /// <param name="producerCount">生产者数量。</param>
        /// <param name="messagesPerProducer">每个生产者的消息数量。</param>
        private static void AssertConcurrentMessages(
            List<ReceivedPacket> packets,
            int producerCount,
            int messagesPerProducer)
        {
            int expectedCount = producerCount * messagesPerProducer;
            Assert.That(packets.Count, Is.EqualTo(expectedCount), "接收消息总数不正确");
            var identities = new HashSet<long>();

            for (int wireIndex = 0; wireIndex < packets.Count; wireIndex++) {
                ReceivedPacket packet = packets[wireIndex];
                Assert.That(packet.header.id, Is.EqualTo(wireIndex), $"线路第{wireIndex}条消息的ID不连续");
                Assert.That(packet.header.format, Is.EqualTo(Format.Raw), $"线路第{wireIndex}条消息格式不正确");
                Assert.That(packet.payload.Length, Is.EqualTo(sizeof(int) * 2), $"线路第{wireIndex}条消息负载长度不正确");

                int producerIndex = BitConverter.ToInt32(packet.payload, 0);
                int sequence = BitConverter.ToInt32(packet.payload, sizeof(int));
                Assert.That(producerIndex, Is.InRange(0, producerCount - 1), $"线路第{wireIndex}条消息生产者序号非法");
                Assert.That(sequence, Is.InRange(0, messagesPerProducer - 1), $"线路第{wireIndex}条消息内部序号非法");

                long identity = ((long)producerIndex << 32) | (uint)sequence;
                Assert.That(identities.Add(identity), Is.True, $"生产者{producerIndex}的第{sequence}条消息重复出现");
            }

            Assert.That(identities.Count, Is.EqualTo(expectedCount), "存在丢失或重复的并发消息");
        }

        /// <summary>
        /// 逐字节比较预期负载和实际负载，并在失败时报告首个差异位置。
        /// </summary>
        /// <param name="expected">预期负载。</param>
        /// <param name="actual">实际负载。</param>
        private static void AssertPayloadEquals(byte[] expected, byte[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), "大消息负载长度不一致");
            for (int i = 0; i < expected.Length; i++) {
                if (actual[i] != expected[i]) {
                    Assert.Fail($"大消息在字节{i}处损坏，预期{expected[i]}，实际{actual[i]}");
                }
            }
        }

        /// <summary>
        /// 保存一次从线路读取的协议头和负载，供测试断言使用。
        /// </summary>
        private sealed class ReceivedPacket
        {
            /// <summary>
            /// 从线路解析出的DNET协议头。
            /// </summary>
            public Header header;

            /// <summary>
            /// 紧随协议头读取的消息负载。
            /// </summary>
            public byte[] payload;
        }

        /// <summary>
        /// 管理一个PeerSocket发送端和原始Socket接收端组成的回环连接。
        /// 创建时使用端口0让操作系统分配临时端口，释放时关闭两端以解除所有阻塞接收。
        /// </summary>
        private sealed class LoopbackPeerSocketPair : IDisposable
        {
            /// <summary>
            /// 初始化已经建立连接的回环Socket对。
            /// </summary>
            /// <param name="sender">DNET发送端。</param>
            /// <param name="receiver">原始Socket接收端。</param>
            private LoopbackPeerSocketPair(PeerSocket sender, Socket receiver)
            {
                Sender = sender;
                Receiver = receiver;
            }

            /// <summary>
            /// 被测DNET发送端。
            /// </summary>
            public PeerSocket Sender { get; private set; }

            /// <summary>
            /// 用于直接检查线路字节的原始Socket接收端。
            /// </summary>
            public Socket Receiver { get; private set; }

            /// <summary>
            /// 创建并连接一个使用系统临时端口的回环Socket对。
            /// </summary>
            /// <returns>已经完成TCP握手的Socket对。</returns>
            public static LoopbackPeerSocketPair Create()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                PeerSocket sender = null;
                Socket receiver = null;
                listener.Start();
                try {
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    Task<Socket> acceptTask = listener.AcceptSocketAsync();
                    sender = new PeerSocket { Name = "PeerSocketSendPumpTests.Sender" };
                    sender.BindRemote(IPAddress.Loopback.ToString(), port);

                    Task connectTask = Task.Run(() => sender.Connect());
                    if (!acceptTask.Wait(OPERATION_TIMEOUT_MILLISECONDS)) {
                        throw new TimeoutException("回环接收端未在限定时间内接受连接");
                    }
                    receiver = acceptTask.GetAwaiter().GetResult();
                    if (!connectTask.Wait(OPERATION_TIMEOUT_MILLISECONDS)) {
                        throw new TimeoutException("PeerSocket未在限定时间内完成回环连接");
                    }
                    connectTask.GetAwaiter().GetResult();
                    receiver.ReceiveTimeout = OPERATION_TIMEOUT_MILLISECONDS;
                    return new LoopbackPeerSocketPair(sender, receiver);
                } catch {
                    receiver?.Dispose();
                    sender?.Dispose();
                    throw;
                } finally {
                    listener.Stop();
                }
            }

            /// <summary>
            /// 关闭连接两端，解除可能仍在等待的异步接收并释放系统句柄。
            /// </summary>
            public void Dispose()
            {
                Sender?.Dispose();
                Receiver?.Dispose();
                Sender = null;
                Receiver = null;
            }
        }

        #endregion
    }
}
