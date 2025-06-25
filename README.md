# DNET

## 简介
[![Build status](https://dev.azure.com/daixian/Pipeline/_apis/build/status/Pipeline%20-%20DNET)](https://dev.azure.com/daixian/Pipeline/_build/latest?definitionId=5)

一个简单的TCP通信库，包含了客户端和服务端，可给Unity使用。可以任意对接自定义的的TCP分包协议。
 
 ## TODO
 考虑升级到.net 4.5+好使用更高效的一些数据结构

## 注意点
1. 实际调用发送IO的时候要整合队列中的所有消息,一次性发送出去.