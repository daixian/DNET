using DNET;
using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 轮询发布消息类。
/// 在Update()中轮询已接收消息队列，查看是否接收到了消息。然后进行消息的分发处理。
/// Logic成员包含所有的处理逻辑。
/// </summary>
public class NetPoll : MonoBehaviour
{
    public Text text1;
    public Text text2;

    /// <summary>
    /// 计时累计，在自动重连中的计时
    /// </summary>
    private float _sumDeltaTime_ARC = 0;

    /// <summary>
    /// 是否已经设置了打印事件
    /// </summary>
    public static bool isSetPrintEvent = false;

    /// <summary>
    /// 调试：我已经发送了的消息条数
    /// </summary>
    //public int _sendCount = 0;

    /// <summary>
    /// 调试：我接收到的消息条数
    /// </summary>
    public int receCount = 0;

    /// <summary>
    /// 是否自动重连
    /// </summary>
    public bool isAutoReConnect = true;

    /// <summary>
    /// 是否当前已经断开连接
    /// </summary>
    private bool _isConnectionBreak = false;

    /// <summary>
    /// 单例
    /// </summary>
    private static NetPoll _instance = null;

    /// <summary>
    /// 获得实例
    /// </summary>
    /// <returns></returns>
    public static NetPoll GetInstance()
    {
        if (_instance == null)
        {
            DxDebug.LogError("NetPoll.GetInstance():还未被实例化!");
        }
        return _instance;
    }

    /// <summary>
    /// 对应SimpleSever.exe
    /// </summary>
    private string IP = "127.0.0.1";
    private int prot = 23333;

    #region Events

    /// <summary>
    /// 事件：与服务器的连接已经断开
    /// </summary>
    public event Action EventConnectionBreak = null;

    /// <summary>
    /// 事件：断线重连成功
    /// </summary>
    public event Action EventReconnectsucceed = null;

    #endregion Events

    /// <summary>
    /// 包含了一些初始化
    /// </summary>
    private void Awake()
    {
        //单例赋值
        _instance = this;
        GameObject.DontDestroyOnLoad(this);

        //随便设置一下
        InitPrint();//初始化日志系统，为了尽早的能够输出日志

#if UNITY_EDITOR
        Screen.SetResolution(854, 480, false);
        DNET.Config.DefaultConfigOnWindows();
        DNET.Config.IsAutoHeartbeat = false;
        //不输出日志了以查看GC(日志产生GC)
        Config.IsLogFile = true;
        Config.IsLogConsole = true;
        DNET.DxDebug.isLog = true;
        DNClient.GetInstance().isDebugLog = false;

#elif UNITY_ANDROID
        //Config.CreatLogFile("");
        Config.IsLogFile = false;
        Config.IsLogConsole = false;
        Config.IsAutoHeartbeat = true;
        //Config.SetCacheDir("");

#elif UNITY_IPHONE
        //Config.CreatLogFile("");
        Config.IsLogFile = false;
        Config.IsLogConsole = false;
        Config.IsAutoHeartbeat = true;
        //Config.SetCacheDir("");
#else
      Screen.SetResolution(854, 480, false);
      DNET.Config.DefaultConfigOnWindows();
#endif
       
    }

    private void Start()
    {
        //挂上ShowFPS脚本
        if (gameObject.GetComponent<ShowFPS>() == null)
            gameObject.AddComponent<ShowFPS>();
        DNClient.GetInstance().Connect(IP, prot); //ywz：10.1.32.81  127.0.0.1
    }

    private void Update()
    {
        MsgUpdate();
        AutoReConnect();
    }

    private void FixedUpdate()
    {
        text1.text = str1;
        text2.text = str2;
    }

    public void OnGUI()
    {
        GUILayout.Label("");
        GUILayout.Label("");//空出来显示fps
        GUILayout.Label("连接状态：" + DNClient.GetInstance().IsConnected);

        //GUILayout.Label("延迟:" + RequestStatus.GetInstance().Delay + "ms");
        GUILayout.Label("SaveGC:" + ByteBufferPool.countSaveGC);
        GUILayout.Label("BadGC:" + ByteBufferPool.countBadGC);
        GUILayout.Label("New:" + ByteBufferPool.countNew);
        if (GUILayout.Button("Exit Application"))
        {
            Application.Quit();
        }

       
    }

    /// <summary>
    /// 一次最多接收128条消息
    /// </summary>
    private ByteBuffer[] dataBuff = new ByteBuffer[128];

    /// <summary>
    /// 消息轮询处理，在update中调用
    /// </summary>
    private void MsgUpdate()
    {
        try
        {
            int msgCount = DNClient.GetInstance().GetReceiveData(dataBuff, 0, dataBuff.Length);
            if (msgCount != 0)
            {
                for (int i = 0; i < msgCount; i++)
                {
                    //递增一个标记，接收到了数据
                    receCount++;

                    ByteBuffer data = dataBuff[i];
                    if (data != null)
                    {
                        //DxDebug.LogConsole("接收到了" + data.validLength + "的数据");

                        //进行消息处理

                        data.Recycle();
                    }
                    else
                    {
                        DxDebug.LogConsole("NetPoll.MsgUpdate():接收到data为null!");
                    }
                }
            }
        }
        catch (Exception e)
        {
            DxDebug.LogConsole("NetPoll.MsgUpdate():执行MsgUpdate()异常");
        }
    }

    /// <summary>
    /// 自动重连处理，在update中调用
    /// </summary>
    private void AutoReConnect()
    {
        try
        {
            _sumDeltaTime_ARC += Time.deltaTime;
            if (_sumDeltaTime_ARC >= 0.2f) //0.2秒的定时，每一秒都会进入
            {
                _sumDeltaTime_ARC = 0;
                //检测到了断线
                if (isAutoReConnect == true && DNClient.GetInstance().IsConnected == false && DNClient.GetInstance().IsConnecting == false)
                {
                    DxDebug.LogWarning("NetPoll.AutoReConnect():当前已经断线!开始自动重连...");
                    _isConnectionBreak = true;//标记当前已经断线
                    if (EventConnectionBreak != null)
                    {
                        EventConnectionBreak();//执行事件当前已经断线
                    }
                    //连一下服务器算了
                    DNClient.GetInstance().Connect(IP, prot); //ywz：10.1.32.81 "127.0.0.1"
                }

                //检测到了自动重连成功
                if (_isConnectionBreak == true && DNClient.GetInstance().IsConnected == true)//此时已经连接成功
                {
                    DxDebug.LogWarning("NetPoll.AutoReConnect():当前已经自动重连成功!");
                    _isConnectionBreak = false;//标记当前不再断线
                    if (EventReconnectsucceed != null)
                    {
                        EventReconnectsucceed();//执行事件当前已经重连成功
                    }
                }

            }
        }
        catch (Exception e)
        {
            DxDebug.LogWarning("NetPoll.AutoReConnect():执行AutoReConnect()异常!" + e.Message);
        }
    }

    /// <summary>
    /// 程序退出的时候必须要关闭
    /// </summary>
    public void OnApplicationQuit()
    {
        Debug.Log("NetPoll.OnApplicationQuit():关闭了应用程序.");

        DNClient.GetInstance().CloseImmediate();
    }

#if UNITY_EDITOR

    /// <summary>
    /// 程序退出的时候必须要关闭(这个函数现在是重复了)
    /// </summary>
    //public void OnDestroy()
    //{
    //    Debug.Log("NetPoll.OnDestroy():关闭了程序。");
    //    DNClient.GetInstance().Close();
    //}

#endif

    #region Print

    /// <summary>
    /// 关联打印到Untiy的控制台系统，可以多次调用，只会生效一次。目的是今早的使日志打印生效
    /// </summary>
    public void InitPrint()
    {
        if (isSetPrintEvent == false)
        {
            isSetPrintEvent = true;
            DxDebug.EventPrint += OnPrint;
        }
        DxDebug.AllMemLogOutput();
        DxDebug.ClearMemLog();
    }

    //由于其他线程不能访问u3d中的组件
    private string str1 = "";

    private string str2 = "";

    /// <summary>
    /// 接入unity的控制台日志系统,要注意这个函数不一定由U3D主线程执行的。
    /// </summary>
    /// <param name="log"></param>
    public void OnPrint(DxDebug.LogItem log)
    {
        if (log.priority >= DxDebug.WarningPriority)
        {
            Debug.LogWarning(log.message);
            if (str2.Length > 1500)
            {
                str2 = "";
            }
            str2 += log.message + "\r\n";
        }
        else if (log.priority >= DxDebug.ConsolePriority)
        {
            Debug.Log(log.message);
            str1 = log.message + "\r\n";
        }
    }

    #endregion Print
}