using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DNET.Test
{
    /// <summary>
    /// 所有的消息类型,看项目定义
    /// </summary>
    public enum ProjectMessageType : int
    {
        None,
        MsgGetProjectUIStatusC2S,
        MsgGetProjectUIStatusS2C,
        MsgU3DRPCProjectUIC2S,
        MsgU3DRPCProjectUIS2C
    }


    public class JsonMessage
    {
        public JsonMessage()
        {
            txid = ++_txid; // 全局自增
        }

        /// <summary>
        /// 消息类型
        /// </summary>
        public ProjectMessageType type;

        /// <summary>
        /// 这一次通信的事务id
        /// </summary>
        public int txid;

        /// <summary>
        /// 全局的计数
        /// </summary>
        private static int _txid = 0;


        public virtual string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public enum ProjectMode
    {
        NoneDef = -1,

        /// <summary>
        /// 未工作,闲置状态
        /// </summary>
        None = 0,

        /// <summary>
        /// 标准模式
        /// </summary>
        Standard,

        /// <summary>
        /// AR模式
        /// </summary>
        AR,

        /// <summary>
        /// 扩展屏模式
        /// </summary>
        ExtendScreen,
    }

    /// <summary>
    /// 请求获取ProjectUI状态 客户端->服务端
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class MsgGetProjectUIStatusC2S : JsonMessage
    {
        public MsgGetProjectUIStatusC2S()
        {
            type = ProjectMessageType.MsgGetProjectUIStatusC2S;
        }

        /// <summary>
        /// 这个字段是u3d顺便发过来的
        /// </summary>
        [JsonProperty]
        public bool u3dIsLR;

        public override string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    /// <summary>
    /// 请求获取ProjectUI状态 服务端->客户端
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class MsgGetProjectUIStatusS2C : JsonMessage
    {
        public MsgGetProjectUIStatusS2C()
        {
            type = ProjectMessageType.MsgGetProjectUIStatusS2C;
        }

        /// <summary>
        /// 当前的Project工作模式
        /// </summary>
        [JsonProperty]
        public ProjectMode ProjectMode;

        /// <summary>
        /// 当前是否正在录屏
        /// </summary>
        [JsonProperty]
        public bool IsRecording;

        public override string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public enum RPCCommand
    {
        /// <summary>
        /// 默认?
        /// </summary>
        None,

        /// <summary>
        /// 标准模式
        /// </summary>
        ClickBtnStandard,

        /// <summary>
        /// AR模式
        /// </summary>
        ClickBtnAR,

        /// <summary>
        /// 扩展屏模式
        /// </summary>
        ClickBtnExtendScreen,

        /// <summary>
        /// 退出
        /// </summary>
        ClickBtnExit,

        /// <summary>
        /// 切换录屏状态
        /// </summary>
        SwitchRecordStatus,
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class MsgU3DRPCProjectUIC2S : JsonMessage
    {
        public MsgU3DRPCProjectUIC2S()
        {
            type = ProjectMessageType.MsgU3DRPCProjectUIC2S;
        }

        [JsonProperty]
        public RPCCommand command;

        public override string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class MsgU3DRPCProjectUIS2C : JsonMessage
    {
        public MsgU3DRPCProjectUIS2C()
        {
            type = ProjectMessageType.MsgU3DRPCProjectUIS2C;
        }

        [JsonProperty]
        public RPCCommand command;

        public override string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public static class BaseMessageExt
    {
        /// <summary>
        /// 发送消息的扩展方法
        /// </summary>
        /// <param name="client"></param>
        /// <param name="message"></param>
        public static void Send(this DNClient client, JsonMessage message)
        {
            // 利用协议的txid和type字段一起发送好了.
            client.Send(message.ToJson(), txrId: message.txid, eventType: (int)message.type);
        }
    }
}
