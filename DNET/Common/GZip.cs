using System;
using System.IO;
using System.IO.Compression;

namespace DNET
{
    /// <summary>
    /// 使用.net内置的GZIP，一个简单的用来压缩数据的类。包含静态方法，在序列化之后，发送前调用即可。
    /// 不能使用它来压缩大文件，最好不要超过1-2M。对于未经压缩过的数据，还是有一定的效果。
    /// 但是对一些已经压缩过的数据可能出现越压越大的情形= =
    /// </summary>
    public class GZip
    {
        /// <summary>
        /// 压缩文件，参数是输入文件路径和输出文件路径。
        /// </summary>
        /// <param name="sourceFile">输入文件路径</param>
        /// <param name="destinationFile">输出文件路径</param>
        public static void CompressFile(string sourceFile, string destinationFile)
        {
            // make sure the source file is there
            if (File.Exists(sourceFile) == false)
                throw new FileNotFoundException();

            // Create the streams and byte arrays needed
            byte[] buffer = null;
            FileStream sourceStream = null;
            FileStream destinationStream = null;
            GZipStream compressedStream = null;

            try
            {
                // Read the bytes from the source file into a byte array
                sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);

                // Read the source stream values into the buffer
                buffer = new byte[sourceStream.Length];
                int checkCounter = sourceStream.Read(buffer, 0, buffer.Length);

                if (checkCounter != buffer.Length)
                {
                    throw new ApplicationException();
                }

                // Open the FileStream to write to
                destinationStream = new FileStream(destinationFile, FileMode.OpenOrCreate, FileAccess.Write);

                // Create a compression stream pointing to the destiantion stream
                compressedStream = new GZipStream(destinationStream, CompressionMode.Compress, true);

                // Now write the compressed data to the destination file
                compressedStream.Write(buffer, 0, buffer.Length);
            }
            catch (ApplicationException e)
            {
                DxDebug.LogWarning("GZip.CompressFile():异常:" + e.Message);
            }
            finally
            {
                // Make sure we allways close all streams
                if (sourceStream != null)
                    sourceStream.Close();

                if (compressedStream != null)
                    compressedStream.Close();

                if (destinationStream != null)
                    destinationStream.Close();
            }
        }

        /// <summary>
        /// 解压缩文件，参数是输入文件路径和输出文件路径。
        /// </summary>
        /// <param name="sourceFile">输入文件路径</param>
        /// <param name="destinationFile">输出文件路径</param>
        public static void DecompressFile(string sourceFile, string destinationFile)
        {
            // make sure the source file is there
            if (File.Exists(sourceFile) == false)
                throw new FileNotFoundException();

            // Create the streams and byte arrays needed
            FileStream sourceStream = null;
            FileStream destinationStream = null;
            GZipStream decompressedStream = null;
            byte[] quartetBuffer = null;

            try
            {
                // Read in the compressed source stream
                sourceStream = new FileStream(sourceFile, FileMode.Open);

                // Create a compression stream pointing to the destiantion stream
                decompressedStream = new GZipStream(sourceStream, CompressionMode.Decompress, true);

                // Read the footer to determine the length of the destiantion file
                quartetBuffer = new byte[4];
                int position = (int)sourceStream.Length - 4;
                sourceStream.Position = position;
                sourceStream.Read(quartetBuffer, 0, 4);
                sourceStream.Position = 0;
                int checkLength = BitConverter.ToInt32(quartetBuffer, 0);

                byte[] buffer = new byte[checkLength + 100];

                int offset = 0;
                int total = 0;

                // Read the compressed data into the buffer
                while (true)
                {
                    int bytesRead = decompressedStream.Read(buffer, offset, 100);

                    if (bytesRead == 0)
                        break;

                    offset += bytesRead;
                    total += bytesRead;
                }

                // Now write everything to the destination file
                destinationStream = new FileStream(destinationFile, FileMode.Create);
                destinationStream.Write(buffer, 0, total);

                // and flush everyhting to clean out the buffer
                destinationStream.Flush();
            }
            catch (ApplicationException e)
            {
                DxDebug.LogWarning("GZip.DecompressFile():异常:" + e.Message);
            }
            finally
            {
                // Make sure we allways close all streams
                if (sourceStream != null)
                    sourceStream.Close();

                if (decompressedStream != null)
                    decompressedStream.Close();

                if (destinationStream != null)
                    destinationStream.Close();
            }

        }


        /// <summary>
        /// 压缩一段数据，这个数据长度不能过大，最好在1M以内
        /// </summary>
        /// <param name="sourceData">用于存储压缩字节的数组</param>
        /// <param name="offset">数组中开始读取的位置</param>
        /// <param name="count">压缩的字节数</param>
        /// <returns></returns>
        public static byte[] CompressBytes(byte[] sourceData, int offset, int count)
        {
            if (sourceData == null)
            {
                return null;
            }

            MemoryStream destinationStream = new MemoryStream();
            GZipStream compressedStream = new GZipStream(destinationStream, CompressionMode.Compress);
            try
            {
                compressedStream.Write(sourceData, offset, count);

                compressedStream.Close();

                byte[] CompressedData = destinationStream.ToArray();
                destinationStream.Close();
                return CompressedData;
            }
            catch (Exception e)
            {
                DxDebug.LogWarning("GZip.CompressBytes():异常:" + e.Message);

            }
            finally
            {
                // Make sure we allways close all streams
                if (destinationStream != null)
                    destinationStream.Close();
            }
            return null;
        }

        /// <summary>
        /// 压缩整段数据
        /// </summary>
        /// <param name="sourceData">源数据</param>
        /// <returns>结果数据</returns>
        public static byte[] CompressBytes(byte[] sourceData)
        {
            return CompressBytes(sourceData, 0, sourceData.Length);
        }

        /// <summary>
        ///  解压缩一段数据，这个数据长度也不能过大
        /// </summary>
        /// <param name="sourceData">用于存储压缩字节的数组</param>
        /// <param name="offset">数组中开始读取的位置</param>
        /// <param name="count">压缩的字节数</param>
        /// <returns>解压缩出来的数据</returns>
        public static byte[] DecompressBytes(byte[] sourceData, int offset, int count)
        {
            if (sourceData == null)
            {
                return null;
            }

            MemoryStream sourceStream = new MemoryStream(sourceData, offset, count);
            GZipStream decompressedStream = new GZipStream(sourceStream, CompressionMode.Decompress);

            //一个重要的得到压缩前文件长度的代码
            byte[] quartetBuffer = new byte[4];
            int position = (int)sourceStream.Length - 4;
            sourceStream.Position = position;
            sourceStream.Read(quartetBuffer, 0, 4);
            sourceStream.Position = 0;
            int checkLength = BitConverter.ToInt32(quartetBuffer, 0); //压缩前数据长度

            byte[] buffer = new byte[checkLength];//解压数据数组
            int bytesRead = -1;
            int seek = 0;
            while (bytesRead != 0)
            {
                if (seek == buffer.Length)
                {
                    break;
                }
                bytesRead = decompressedStream.Read(buffer, seek, buffer.Length - seek);
                seek += bytesRead;
            }
            return buffer;
        }

        /// <summary>
        /// 解压缩一段数据，这个数据长度也不能过大
        /// </summary>
        /// <param name="sourceData">源数据</param>
        /// <returns>解压缩出来的数据</returns>
        public static byte[] DecompressBytes(byte[] sourceData)
        {
            return DecompressBytes(sourceData, 0, sourceData.Length);
        }
    }


}
