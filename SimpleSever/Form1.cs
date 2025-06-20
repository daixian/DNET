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

            isReSend = this.checkBox1.Checked;

            //Config.DefaultConfigOnWindows();//在自己根目录下创建日志
            Config.IsAutoHeartbeat = false;

            DNServer.Inst.EventPeerReceData += OnTokenReceData;
            DNServer.Inst.Start(23333);
        }

        Random rand = new Random();

        bool isReSend;

        private void OnTokenReceData(Peer peer)
        {
            byte[][] datas = peer.GetReceiveData();
            if (datas == null) {
                return;
            }

            for (int i = 0; i < datas.Length; i++) {
                byte[] data = datas[i];
                if (isReSend) //如果CheckBox选择了要回发
                {
                    //直接回发
                    DNServer.Inst.Send(peer, data);
                }
                //得到消息类型然后处理
                //int pType = BitConverter.ToInt32(data, 0);
                //TypeRegister.GetInstance().Dispatch(token, pType, data, sizeof(int), data.Length - sizeof(int));
            }
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            isReSend = this.checkBox1.Checked;
        }
    }
}
