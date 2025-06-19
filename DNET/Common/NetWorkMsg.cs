using System;

namespace DNET
{
    /// <summary>
    /// Unity3d主模块和通信模块的工作线程之间异步工作使用的消息,
    /// 一般不应该使用这个，而应该直接使用提供的方法.
    /// </summary>
    internal class NetWorkMsg
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        public enum Tpye
        {
            //---------------客户端-----------------
            /// <summary>
            /// 连接
            /// </summary>
            C_Connect,

            /// <summary>
            /// 向服务器发送
            /// </summary>
            C_Send,

            /// <summary>
            /// 接收事件消息
            /// </summary>
            C_Receive, //将接收到缓存的数据进行处理

            /// <summary>
            /// 异步的关闭客户端
            /// </summary>
            C_AsynClose,

            //---------------服务器-----------------
            /// <summary>
            /// 开始服务器
            /// </summary>
            S_Start,

            /// <summary>
            /// 认证消息
            /// </summary>
            S_Accept,

            /// <summary>
            /// 向某个token发送
            /// </summary>
            S_Send,

            /// <summary>
            /// 接收事件
            /// </summary>
            S_Receive,

            /// <summary>
            /// 开始向所有用户的一次发送。自动发送这些用户待发送队列中的数据
            /// </summary>
            S_SendAll
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="type">线程用来判断的类型</param>
        /// <param name="data">如果需要的话会是一个数据</param>
        /// <param name="arg1">附加参数（服务器端用于记录了TokenID） </param>
        /// <param name="isDataCopy">是否进行数据的拷贝（可控为了提高性能）</param>
        public NetWorkMsg(Tpye type, byte[] data = null, int arg1 = int.MaxValue, bool isDataCopy = false)
        {
            this.type = type;
            this.arg1 = arg1;
            if (data != null && isDataCopy) //做一个拷贝
            {
                this.data = new byte[data.Length];
                Buffer.BlockCopy(data, 0, this.data, 0, data.Length);
            }
            else {
                this.data = data; //不作拷贝了
            }

            timeTickCreat = DateTime.Now.Ticks;
        }

        /// <summary>
        /// 构造函数，这个MSG一定会作一个拷贝
        /// </summary>
        /// <param name="type">线程用来判断的类型</param>
        /// <param name="data">如果需要的话会是一个数据</param>
        /// <param name="offset">数据的起始位置</param>
        /// <param name="count">数据的长度</param>
        /// <param name="arg1">附加参数（服务器端用于记录了TokenID）</param>
        public NetWorkMsg(Tpye type, byte[] data, int offset, int count, int arg1 = int.MaxValue)
        {
            this.type = type;
            this.arg1 = arg1;
            if (data != null) //做一个拷贝
            {
                this.data = new byte[count];
                Buffer.BlockCopy(data, offset, this.data, 0, count);
            }
            else {
                data = null;
            }

            timeTickCreat = DateTime.Now.Ticks;
        }

        /// <summary>
        /// 这个消息的类型
        /// </summary>
        public Tpye type;

        /// <summary>
        /// 数据
        /// </summary>
        public byte[] data;

        /// <summary>
        /// 附加参数（服务器端用于记录了TokenID）
        /// </summary>
        public int arg1;

        /// <summary>
        /// 附加参数
        /// </summary>
        public string text1;

        /// <summary>
        /// 这个字段一般被直接赋值了，应该合并这个字段和arg1字段
        /// </summary>
        public Token token;

        /// <summary>
        /// 创建时的timeTick
        /// </summary>
        public long timeTickCreat;

        //public int arg2;

        /// <summary>
        /// 重置数据
        /// </summary>
        public void Reset(Tpye type = Tpye.C_Connect, byte[] data = null, int arg1 = 0, Token token = null)
        {
            this.type = type;
            this.data = data;
            this.arg1 = arg1;
            this.text1 = null;
            this.token = null;
            timeTickCreat = DateTime.Now.Ticks;
        }
    }
}
