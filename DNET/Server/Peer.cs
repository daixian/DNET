using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DNET.Protocol;

namespace DNET
{
    //delegate void ProcessData(SocketAsyncEventArgs args);

    /// <summary>
    /// 用户对象,也就是一个TCP连接的一个端点.Peer里面起码要封装一个Socket对象.
    /// 它最主要的就是有一个ID.
    /// </summary>
    public sealed class Peer : IDisposable
    {

        /// <summary>
        /// 现在由于在客户端也添加了一个Token，用于在协议事件的时候方便统一逻辑，当初始化客户端的token的时候调用这个构造方法
        /// </summary>
        internal Peer()
        {
            peerSocket = new PeerSocket();
        }
        /// <summary>
        /// 它的ID，这个ID会一直递增的被分配
        /// </summary>
        public int ID {
            get;
            //这个ID由于是在DNET库中被分配，所以考虑应该设置为internal
            internal set;
        }

        /// <summary>
        /// 用户自定义的绑定对象，用于简单的绑定关联一个对象
        /// </summary>
        public object user { get; set; }

        /// <summary>
        /// 拥有这个token的DNClient对象。
        /// </summary>
        public PeerSocket peerSocket { get; internal set; }

        /// 它的状态.
        /// </summary>
        public PeerStatus Status => peerSocket.peerStatus;

        /// <summary>
        /// 发送队列是否太长
        /// </summary>
        public bool SendQueueOverflow {
            get {
                if (peerSocket.WaitSendMsgCount >= 64)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// 获取这个token的接收数据
        /// </summary>
        /// <returns></returns>
        public List<Message> GetReceiveData() => peerSocket.GetReceiveMessages();


        #region IDisposable  

        public void Dispose()
        {
            try {
                //执行Dispose事件
                //if (EventDispose != null) //事件
                //{
                //    try {
                //        EventDispose(this);
                //    } catch (Exception e) {
                //        LogProxy.LogWarning("Peer.Dispose()：执行事件EventDispose异常！" + e.Message);
                //    }
                //}

                peerSocket.Dispose();
            } catch (Exception) {
                //不要的客户端，不抛出错误，直接Close()
            } finally {

            }
        }

        #endregion IDisposable Members
    }
}
