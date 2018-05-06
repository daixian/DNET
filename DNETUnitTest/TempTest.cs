using DNET;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DNETUnitTest
{
    public partial class UnitTest1
    {
        //[TestMethod]
        public void TestMethod_Temp1()
        {
            unsafe
            {
                int[] buffer = new int[1024 * 128];
                IntPtr addr = Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0);
                int* p = (int*)addr.ToPointer();
                for (int count = 0; count < 1024; count++)
                {
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        p[i] = i;
                    }
                }

            }
        }

        //[TestMethod]
        public void TestMethod_Temp2()
        {
            unsafe
            { 
 
            }
        }
    }
}