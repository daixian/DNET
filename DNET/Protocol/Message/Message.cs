using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DNET.Protocol
{
    /// <summary>
    /// 一条消息.
    /// </summary>
    public class Message
    {
        /// <summary>
        /// 实际的头,它其实也是各种字段.
        /// </summary>
        public Header header;

        /// <summary>
        /// 数据
        /// </summary>
        public byte[] data;
    }
}
