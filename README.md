# DNET

## 简介
[![Build](https://github.com/daixian/DNET/actions/workflows/build.yml/badge.svg)](https://github.com/daixian/DNET/actions/workflows/build.yml)
[![Release](https://github.com/daixian/DNET/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/daixian/DNET/actions/workflows/build-and-release.yml)


一个简单的TCP通信库，包含了客户端和服务端，可给Unity3D使用。里面有一个自己规定的分包的协议。


## 需要实现的特性备忘
1. 实际调用发送IO的时刻要整合队列中的所有消息,一次性发送出去.
2. 如果一次性发送的需要的buffer长度过长,那么就停止从队列中提取.
3. 对各种byte[]数组和网络Message使用了对象池以减少GC.


## 测试
下载 [nunit3-console.exe](https://nunit.org/download/).
执行脚本类似下面这个,C:\temp\nunit_work这个文件夹里每次运行都会生成一个文件.
``` bat
@echo off
setlocal

:loop
echo Running test at %time%

"C:\soft\NUnit.Console-3.20.1\bin\net462\nunit3-console.exe" "C:\Users\Administrator\Desktop\DNET.Test\Release\DNET.Test.dll" --test=DNET.Test.ServerClientTest.TestMethod_ServerClient --work=C:\temp\nunit_work

if errorlevel 1 (
    echo ❌ Test failed at %time%, stopping loop.
    pause
    exit /b 1
)

echo ✅ Test passed, continuing...
echo.
goto loop

```