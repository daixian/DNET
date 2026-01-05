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

            _ = this.checkBox1.Checked;

            //Config.DefaultConfigOnWindows();//在自己根目录下创建日志
            Config.IsAutoHeartbeat = false;
            LogProxy.SetupLogToConsole();
            // 在vs调试的时候这个控制台内容会打印到vs的IDE中.
            DNServer.Inst.EventPeerReceData += OnTokenReceData;
            DNServer.Inst.Start(23333);
        }


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
                peer.AddSendData(msg.data.buffer, 0, msg.data.Length,
                    format: msg.Format,
                    txrId: msg.TxrId);
            }
            server.TryStartSend(peer); // 此时再合并发送.
            msgList.RecycleAllItems();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            _ = this.checkBox1.Checked;
        }
    }
}
