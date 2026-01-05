using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DNET;

namespace SimpleSever
{
    /// <summary>
    /// 一个简单实验服务器，收到消息之后回发这条消息。
    /// </summary>
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            _enableEcho = this.checkBox1.Checked;

            Config.IsDebugMode = false;
            Config.IsAutoHeartbeat = true;

            // 在vs调试的时候这个控制台内容会打印到vs的IDE中.
            LogProxy.SetupLogToConsole();

        }

        bool _enableEcho = true;


        private void OnTokenReceData(DNServer server, Peer peer)
        {
            var msgList = peer.GetReceiveData();
            if (msgList == null || msgList.Count == 0) return;

            foreach (var msg in msgList) {
                // 这是小线程的回调事件,server不应该sleep
                // while (peer.IsSendQueueOverflow())
                //     Thread.Sleep(1);

                if (msg.Format == Format.Text) {
                    LogProxy.Info($"收到文本数据:{msg.Text},事务ID{msg.TxrId}");
                }
                // 回发接收到的数据
                if (_enableEcho) {
                    peer.AddSendData(msg.data.Bytes, 0, msg.data.Length,
                        format: msg.Format,
                        txrId: msg.TxrId);
                }
            }
            server.TryStartSend(peer); // 此时再合并发送.
            msgList.RecycleAllItems();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            _enableEcho = this.checkBox1.Checked;
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(textBoxPort.Text, out int port)) {
                MessageBox.Show("请输入有效的端口号。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            DNServer.Inst.PeerReceived += OnTokenReceData;
            DNServer.Inst.Start(port);
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            DNServer.Inst.Close();
            DNServer.Inst.PeerReceived -= OnTokenReceData;
        }
    }
}
