using DNET;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class test : MonoBehaviour {

    public UnityEngine.UI.InputField inputField;

    public NetPoll netpoll;
    public int sendCount = 0;//表示消息条数

    // Use this for initialization
    void Start()
    {
        //默认一帧发100条
        inputField.text = "" + 100;

    }

    float _timeCount = 0;

    float timeInterval = 0.001f;//默认0.01秒，差不多就是每帧

    void Update()
    {
        _timeCount += Time.deltaTime;

        if (_timeCount > timeInterval)//每0.01秒一次
        {
            _timeCount = 0;

            int sendCount = Convert.ToInt32(inputField.text);

            for (int i = 0; i < sendCount; i++)
            {
                SendTestMsg(); 
                //Console.Write("GC测试啊" + 123);
            }

            //while (UnityEngine.Random.value > 0.05)//只要大于0.2就随机再发
            //{
            //    SendTestMsg();
            //}
        }
    }


    public void onClick()
    {
        netpoll.enabled = !netpoll.enabled;

    }

    byte[] sendData = new byte[512];

    /// <summary>
    /// 发送数据
    /// </summary>
    void SendTestMsg()
    {
        if (DNET.DNClient.GetInstance().IsConnected)
        {
            if (DNET.DNClient.GetInstance().isSendQueueIsFull)
            {
                return;
            }

            sendCount++;
            DNET.DNClient.GetInstance().Send(sendData);

        }

    }



}
