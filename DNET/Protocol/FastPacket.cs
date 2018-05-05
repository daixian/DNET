using System;
using System.Collections.Generic;

namespace DNET
{
    /// <summary>
    /// 一种比较快速的打包方式，只用一个数据长度的int作分割(这个写的数据长度值不包含这个int头的长度),
    /// 非常常见的分包方式。
    /// </summary>
    public class FastPacket : IPacket
    {
        byte[] IPacket.PrePack(byte[] data, int index, int length)
        {
            byte[] packedData = new byte[length + sizeof(int)];
            Buffer.BlockCopy(BitConverter.GetBytes((int)length), 0, packedData, 0, sizeof(int));
            Buffer.BlockCopy(data, index, packedData, sizeof(int), length);
            return packedData;
        }

        byte[] IPacket.CompletePack(byte[] data)
        {
            //什么也不需要做了
            return data;
        }

        byte[] IPacket.Pack(byte[] data)
        {
            byte[] packedData = new byte[data.Length + sizeof(int)];
            Buffer.BlockCopy(BitConverter.GetBytes((int)data.Length), 0, packedData, 0, sizeof(int));
            Buffer.BlockCopy(data, 0, packedData, sizeof(int), data.Length);
            return packedData;
        }

        byte[] IPacket.UnPack(byte[] sData, int startIndex)
        {
            int length = BitConverter.ToInt32(sData, 0);
            byte[] data = new byte[length];
            Buffer.BlockCopy(sData, startIndex + sizeof(int), data, 0, data.Length);
            return data;
        }

        FindPacketResult IPacket.FindPacket(byte[] sData, int startIndex)
        {
            FindPacketResult result = new FindPacketResult();
            if (sData.Length - startIndex < sizeof(int))
            {
                result.dataArr = null;
                result.reserveData = sData;
            }

            int index = startIndex;

            List<byte[]> listData = new List<byte[]>();

            while (index < sData.Length)
            {
                //得到一个长度
                int length = BitConverter.ToInt32(sData, index);
                if (sData.Length - index - sizeof(int) < length)//表示还没有接收完
                {
                    if (index == 0)
                    {
                        result.reserveData = sData;
                    }
                    else
                    {
                        result.reserveData = new byte[sData.Length - index];
                        Buffer.BlockCopy(sData, index, result.reserveData, 0, result.reserveData.Length);
                    }
                }
                else
                {
                    byte[] data = new byte[length];
                    Buffer.BlockCopy(sData, index + sizeof(int), data, 0, data.Length);
                    listData.Add(data);
                }
                index += length + sizeof(int);
            }

            result.dataArr = listData.ToArray();
            return result;
        }
    }
}