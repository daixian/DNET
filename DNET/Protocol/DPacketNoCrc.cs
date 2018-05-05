using System;
using System.Collections.Generic;
using System.Text;

namespace DNET
{
    /// <summary>
    /// 一种C/S两端的基本通信协议
    /// 数据头 + 长度（整个包总长度）  + 数据域  + 数据尾
    /// </summary>
    public class DPacketNoCrc : IPacket
    {
        #region Fields

        /// <summary>
        /// 帧头
        /// </summary>
        private static readonly byte[] head = Encoding.ASCII.GetBytes("*FsT");

        /// <summary>
        /// 帧尾
        /// </summary>
        private static readonly byte[] end = Encoding.ASCII.GetBytes("FeNd");

        #endregion Fields

        #region BuiltIn Function

        /// <summary>
        /// 输入一段有头尾的数据包，尝试拆出数据
        /// </summary>
        private byte[] UnPack(byte[] PData, OnceFindResult result)
        {
            if (result.e == PacketError.Succeed)
            {
                byte[] data = new byte[result.length - head.Length - sizeof(int) - end.Length];
                Buffer.BlockCopy(PData, result.startIndex + head.Length + sizeof(int), data, 0, data.Length);
                return data;
            }
            else
            {
                DxDebug.LogWarning("DPacket.UnPack():拆出数据失败！");
                return null;
            }
        }

        /// <summary>
        /// 从数据包中得到Length
        /// </summary>
        private int GetLength(byte[] PacketData, int startIndex = 0)
        {
            return BitConverter.ToInt32(PacketData, head.Length + startIndex);
        }

        /// <summary>
        /// 检查数据包的头是否正确
        /// </summary>
        private bool CheckHead(byte[] PacketData, int startIndex = 0)
        {
            for (int i = 0; i < head.Length; i++)
            {
                if (PacketData[startIndex + i] != head[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 检查数据包的尾是否正确startIndex+length要等于这个数据包的末尾
        /// </summary>
        private bool CheckEnd(byte[] data, int startIndex, int length)
        {
            for (int i = 0; i < end.Length; i++)
            {
                if (data[startIndex + length - end.Length + i] != end[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 检测测数据包是否正确的功能函数。输入一个判断位置，会检测包头，长度，包尾巴，然后返回结果。
        /// </summary>
        /// <param name="PacketData"></param>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        private OnceFindResult PacketCheck(byte[] PacketData, int startIndex = 0)
        {
            OnceFindResult reslut = new OnceFindResult();
            reslut.startIndex = startIndex;

            if ((PacketData.Length - startIndex) < (head.Length + sizeof(int) + 0 + end.Length))
            {
                reslut.e = PacketError.PacketLengthTooShort;//长度就小于最小长度
                return reslut;
            }
            //判断包头是否正确
            else if (!CheckHead(PacketData, startIndex))
            {
                reslut.e = PacketError.HeadError;//包头字节不对
                return reslut;
            }
            int length = GetLength(PacketData, startIndex);//从数据包中得到长度
            if (startIndex + length > PacketData.Length)
            {
                reslut.e = PacketError.PacketReceiveing;//长度位数据不对，还未收完整
                reslut.length = length;
                return reslut;
            }
            if (!CheckEnd(PacketData, startIndex, length))
            {
                reslut.e = PacketError.EndError;//包尾字节不对
                return reslut;
            }

            reslut.e = PacketError.Succeed;
            reslut.length = length;
            return reslut;
        }

        /// <summary>
        /// 检测测数据包是否正确的错误信息
        /// </summary>
        private enum PacketError
        {
            /// <summary>长度就小于最小长度</summary>
            PacketLengthTooShort,

            /// <summary>包头字节不对</summary>
            HeadError,

            /// <summary>包尾字节不对</summary>
            EndError,

            /// <summary>数据包还正在接受，有帧头，但是长度还不够</summary>
            PacketReceiveing,

            /// <summary>成功</summary>
            Succeed
        }

        /// <summary>
        /// 从一段数据流中从前至后查找数据包，返回第一个数据包的位置,如果出现第一个未接收完整的疑似包，
        /// 而该疑似包之后没有再发现下一个数据包，则传出这个疑似包的结果。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        private OnceFindResult FindPacketOnce(byte[] data, int startIndex)
        {
            OnceFindResult result = new OnceFindResult();
            int startSuspect = 0;
            bool isFindSuspect = false;
            for (int i = startIndex; i < data.Length; i++)
            {
                result = PacketCheck(data, i);
                if (result.e == PacketError.Succeed) //如果当前位置判断成功，就直接传出结果
                {
                    return result;
                }
                else if (result.e == PacketError.PacketReceiveing && isFindSuspect == false)
                {
                    startSuspect = i;
                    isFindSuspect = true;
                    break;
                }
                else if (result.e == PacketError.PacketLengthTooShort && isFindSuspect == false)
                {
                    startSuspect = i;
                    isFindSuspect = true;
                    break;
                }
            }
            if (isFindSuspect)
            {
                return PacketCheck(data, startSuspect);
            }
            else
            {
                return result;
            }
        }

        #endregion BuiltIn Function

        /// <summary>
        ///寻找一次数据包结果的结构体
        /// </summary>
        private struct OnceFindResult
        {
            internal PacketError e;

            /// <summary>
            /// 如果结果是成功，结果的起始位置
            /// </summary>
            internal int startIndex;

            /// <summary>
            /// 结果的长度
            /// </summary>
            internal int length;
        }

        #region IPacket implementation

        byte[] IPacket.PrePack(byte[] data, int index, int length)
        {
            byte[] packetData = new byte[head.Length + sizeof(int) + length + end.Length];
            int curIndex = 0;
            Buffer.BlockCopy(head, 0, packetData, curIndex, head.Length); //数据头
            curIndex += head.Length;
            Buffer.BlockCopy(BitConverter.GetBytes(packetData.Length), 0, packetData, curIndex, sizeof(int)); //长度，是整个打包后的包长度
            curIndex += sizeof(int);
            Buffer.BlockCopy(data, index, packetData, curIndex, length); //数据域
            curIndex += data.Length;
            Buffer.BlockCopy(end, 0, packetData, curIndex, end.Length); //数据尾

            return packetData;
        }

        byte[] IPacket.CompletePack(byte[] data)
        {
            //什么也不用做了
            return data;
        }

        byte[] IPacket.Pack(byte[] data)
        {
            byte[] PacketData = new byte[head.Length + sizeof(int) + data.Length + end.Length];
            int curIndex = 0;
            Buffer.BlockCopy(head, 0, PacketData, curIndex, head.Length); //数据头
            curIndex += head.Length;
            Buffer.BlockCopy(BitConverter.GetBytes(PacketData.Length), 0, PacketData, curIndex, sizeof(int)); //长度，是整个打包后的包长度
            curIndex += sizeof(int);
            Buffer.BlockCopy(data, 0, PacketData, curIndex, data.Length); //数据域
            curIndex += data.Length;
            Buffer.BlockCopy(end, 0, PacketData, curIndex, end.Length); //数据尾

            return PacketData;
        }

        byte[] IPacket.UnPack(byte[] sData, int startIndex)
        {
            OnceFindResult result = PacketCheck(sData, startIndex);
            if (result.e == PacketError.Succeed)
            {
                byte[] data = new byte[result.length - head.Length - sizeof(int) - end.Length];
                Buffer.BlockCopy(sData, result.startIndex + head.Length + sizeof(int), data, 0, data.Length);
                return data;
            }
            else
            {
                return null;
            }
        }

        FindPacketResult IPacket.FindPacket(byte[] sData, int startIndex)
        {
            // DxDebug.LogConsole("DPacket.FindPacket(): sData长度:" + sData.Length + " startIndex: " + startIndex);
            List<byte[]> packsList = new List<byte[]>(); //只能这样了，不然线程不安全
            FindPacketResult findPacketResult = new FindPacketResult();
            findPacketResult.dataArr = null;
            findPacketResult.reserveData = null;
            //从头至尾依次检查
            int checkStart = startIndex;
            while (checkStart < sData.Length)
            {
                DPacketNoCrc.OnceFindResult result = FindPacketOnce(sData, checkStart);

                if (result.startIndex != checkStart)
                {
                    DxDebug.LogWarning("DPacketNoCrc.FindPacket():丢弃了一段数据，丢弃起始" + checkStart +
                        "丢弃长度" + (result.startIndex - checkStart));
                }

                if (result.e == DPacketNoCrc.PacketError.Succeed)
                {
                    packsList.Add(UnPack(sData, result)); //解包，添加到队列
                    // DxDebug.Log("DPacket.FindPacket():解包成功");
                    checkStart = result.startIndex + result.length; //解包成功，直接跳下一个长度位置
                }
                else if (result.e == DPacketNoCrc.PacketError.PacketReceiveing)
                {
                    findPacketResult.reserveData = new byte[sData.Length - result.startIndex];
                    Buffer.BlockCopy(sData, result.startIndex, findPacketResult.reserveData, 0, findPacketResult.reserveData.Length);
                    break;
                }
                else if (result.e == DPacketNoCrc.PacketError.PacketLengthTooShort)
                {
                    findPacketResult.reserveData = new byte[sData.Length - result.startIndex];
                    Buffer.BlockCopy(sData, result.startIndex, findPacketResult.reserveData, 0, findPacketResult.reserveData.Length);
                    DxDebug.Log("DPacketNoCrc.FindPacket():解包：数据太短");
                    break;
                }
            }
            findPacketResult.dataArr = packsList.ToArray();
            packsList.Clear();
            return findPacketResult;
        }

        #endregion IPacket implementation
    }
}