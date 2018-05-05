using System;
using System.Collections.Generic;


namespace DNET
{
    /// <summary>
    /// 客户端和服务器之间通信的数据打包方法的接口
    /// </summary>
    public interface IPacket
    {

        /// <summary>
        /// 预打包，为了减少内存分配。
        /// 创建一段打包长度的数据，将用户数据放在这个更长的数据的相应的位置。
        /// 然后在之后的操作中调用CompletePack，完成这个数据的打包。
        /// 注意：这一步是由“用户线程”执行的。
        /// </summary>
        /// <param name="data">用户要传输的数据</param>
        /// <param name="index">起始位置</param>
        /// <param name="length">长度</param>
        /// <returns>与打包结果空间大小一致的数据</returns>
        byte[] PrePack(byte[] data, int index, int length);

        /// <summary>
        /// 从一个预打包数据中，完成这次打包。
        /// 注意：这一步由“打包线程”执行的。
        /// </summary>
        /// <param name="data">预打包结果数据</param>
        /// <returns>最终打包结果</returns>
        byte[] CompletePack(byte[] data);

        /// <summary>
        /// 将数据打包成数据包，输入要传输的数据，输出打包之后的数据
        /// </summary>
        /// <param name="data">要传输的数据</param>
        /// <returns>打包后的数据包</returns>
        byte[] Pack(byte[] data);

        /// <summary>
        /// 从数据流的某一处为起点，尝试解包一次数据
        /// </summary>
        /// <param name="sData">数据包数据流</param>
        /// <param name="startIndex">解包起点</param>
        /// <returns></returns>
        byte[] UnPack(byte[] sData, int startIndex = 0);

        /// <summary>
        /// 从数据流的某一处为起点， 返回一个当前流里所有的数据包的解包结果
        /// </summary>
        /// <param name="sData">数据包数据流</param>
        /// <param name="startIndex">解包起点</param>
        /// <returns></returns>
        FindPacketResult FindPacket(byte[] sData, int startIndex = 0);

    }

    /// <summary>
    /// 寻找数据包结果结构体。包含:寻找结果和保留作下次的数据
    /// </summary>
    public struct FindPacketResult
    {
        /// <summary>
        /// 寻找到的结果,正确的数据包
        /// </summary>
        public byte[][] dataArr;

        /// <summary>
        /// 不能判断所以仍然应该保留的数据：如接收了一半的数据包，作为下次数据流的起始
        /// </summary>
        public byte[] reserveData;

    }


}
