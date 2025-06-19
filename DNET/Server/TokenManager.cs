using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace DNET
{
    /// <summary>
    /// 要负责管理已连接的客户端的个数
    /// </summary>
    public class TokenManager
    {
        #region Constructor

        /// <summary>
        /// 构造函数
        /// </summary>
        public TokenManager()
        {
            if (_instance != null) {
                DxDebug.LogWarning("TokenManager是个单例，被多次调用构造函数");
                //this.Dispose();
                //_instance = null;
            }
        }

        private static TokenManager _instance = new TokenManager();

        /// <summary>
        /// 获得实例
        /// </summary>
        /// <returns></returns>
        public static TokenManager GetInstance()
        {
            return _instance;
        }

        /// <summary>
        /// 获得实例
        /// </summary>
        /// <returns></returns>
        public static TokenManager GetInst()
        {
            return _instance;
        }

        #endregion Constructor

        #region Fields

        /// <summary>
        /// 所有用户的字典，key是token的ID
        /// </summary>
        private Dictionary<int, Token> _dictToken = new Dictionary<int, Token>();

        /// <summary>
        /// 字典的锁
        /// </summary>
        private object _lockDict = new object();

        /// <summary>
        /// 是否当前字典和列表相等了,用于在GetAllToken()遍历所有用户的时候，如果相等了就不需要再次重新生成列表
        /// </summary>
        private bool _isDictEqualArr = false;

        /// <summary>
        /// 所有用户的数组
        /// </summary>
        private Token[] _arrToken = null;

        /// <summary>
        /// 一个递增的ID计数，会分配给新的Token
        /// </summary>
        private int _curID = 0;

        /// <summary>
        /// disposed标志
        /// </summary>
        private bool disposed;

        #endregion Fields

        #region Property

        /// <summary>
        /// 当前用户的计数
        /// </summary>
        public int TokensCount { get { return _dictToken.Count; } }

        #endregion Property

        #region Event

        /// <summary>
        /// 事件：新连接上了一个客户。
        /// 参数int: Token的id
        /// </summary>
        public event Action<int> EventAddToken;

        /// <summary>
        /// 事件：删除/关闭了一个客户。
        /// 参数int: Token的id
        /// 参数TokenErrorType: 删除原因
        /// </summary>
        public event Action<int, TokenErrorType> EventDeleteToken;

        #endregion Event

        #region internal Function

        /// <summary>
        /// 添加一个token
        /// </summary>
        /// <param name="token"></param>
        internal Token AddToken(Token token)
        {
            lock (this._lockDict) {
                token.ID = _curID;
                _dictToken.Add(token.ID, token);
                //递增ID计数，额，不过上面已经加锁了
                Interlocked.Increment(ref _curID);

                _isDictEqualArr = false; //标记当前字典和列表已经不一致了
            }
            if (EventAddToken != null) //事件
            {
                try {
                    EventAddToken(token.ID);
                } catch (Exception e) {
                    DxDebug.LogWarning("TokenManager.AddToken()：执行事件EventAddToken异常！" + e.Message);
                }
            }
            DxDebug.LogConsole(String.Format("TokenManager.AddToken()：添加了一个客户端. 当前服务器上有{0}个客户端; ip:{1}", _dictToken.Count, token.IP));
            return token;
        }

        /// <summary>
        /// 关闭一个token，释放token的资源，但是不会从当前列表中删除这个token
        /// </summary>
        /// <param name="id">Token的id</param>
        /// <param name="type">错误原因</param>
        private void CloseToken(int id, TokenErrorType type)
        {
            Token token = GetToken(id);
            if (token != null) {
                token.Dispose();
            }
        }

        /// <summary>
        /// 内部的删除一个Token，原因有：
        /// 发生了SocketError导致，内部的删除一个Token（SocketError）
        /// </summary>
        /// <param name="id"></param>
        /// <param name="socketError">错误参数是SocketError</param>
        internal void DeleteToken(int id, SocketError socketError)
        {
            CloseToken(id, TokenErrorType.SocketError); //先关闭
            lock (this._lockDict) {
                if (_dictToken.ContainsKey(id)) {
                    _dictToken.Remove(id);

                    _isDictEqualArr = false; //标记当前字典和列表已经不一致了
                }
                else {
                    return;
                }
            }
            if (EventDeleteToken != null) //事件
            {
                try {
                    EventDeleteToken(id, TokenErrorType.SocketError);
                } catch (Exception e) {
                    DxDebug.LogWarning("TokenManager.DeleteToken()：执行事件EventDeleteToken异常！" + e.Message);
                }
            }

            DxDebug.LogConsole(String.Format("TokenManager．DeleteToken()：关闭了一个客户端. 还有{0}个客户端，SocketError:{1}", _dictToken.Count, socketError.ToString()));
        }

        /// <summary>
        /// 内部的删除一个Token，原因有：
        /// 1、函数返回了0，远端已经关闭了这个连接
        /// 2、长时间没有收到心跳包
        /// 3、deleteAllToken()
        /// </summary>
        /// <param name="id"></param>
        /// <param name="errorType">错误参数是TokenErrorType</param>
        internal void DeleteToken(int id, TokenErrorType errorType)
        {
            CloseToken(id, errorType); //先关闭
            lock (this._lockDict) {
                if (_dictToken.ContainsKey(id)) {
                    _dictToken.Remove(id);

                    _isDictEqualArr = false; //标记当前字典和数组已经不一致了
                }
                else {
                    return;
                }
            }
            if (EventDeleteToken != null) //事件
            {
                try {
                    EventDeleteToken(id, errorType);
                } catch (Exception e) {
                    DxDebug.LogWarning("TokenManager.DeleteToken()：执行事件EventDeleteToken异常！" + e.Message);
                }
            }

            DxDebug.LogConsole(String.Format("TokenManager．DeleteToken()：关闭了一个客户端. 还有{0}个客户端，原因{1}", _dictToken.Count, errorType.ToString()));
        }

        #endregion internal Function

        #region Exposed Function

        /// <summary>
        /// 删除一个token，会自动关闭连接。会产生事件
        /// </summary>
        /// <param name="id">根据ID删除已个Token</param>
        public void DeleteToken(int id)
        {
            CloseToken(id, TokenErrorType.UserDelete); //先关闭
            lock (this._lockDict) {
                if (_dictToken.ContainsKey(id)) {
                    _dictToken.Remove(id);

                    _isDictEqualArr = false; //标记当前字典和数组已经不一致了
                }
                else {
                    return;
                }
            }
            if (EventDeleteToken != null) //事件
            {
                try {
                    EventDeleteToken(id, TokenErrorType.UserDelete); //这个是外部的调用删除
                } catch (Exception e) {
                    DxDebug.LogWarning("TokenManager.DeleteToken()：执行事件EventDeleteToken异常！" + e.Message);
                }
            }

            DxDebug.LogConsole(String.Format("TokenManager．DeleteToken()：关闭了一个客户端. 还有{0}个客户端，原因{1}", _dictToken.Count, TokenErrorType.UserDelete.ToString()));
        }

        /// <summary>
        /// 删除所有客户端，目前在关闭了服务器的时候会调用
        /// </summary>
        public void DeleteAllToken()
        {
            DxDebug.LogWarning(String.Format("TokenManager.DeleteAllToken()：删除所有客户端！"));
            while (true) {
                Token[] tokens = GetAllToken();
                if (tokens == null) {
                    return; //确保没有token了
                }
                for (int i = 0; i < tokens.Length; i++) {
                    Token token = tokens[i];
                    DeleteToken(token.ID, TokenErrorType.ClearAllToken);
                }

                _isDictEqualArr = false; //标记当前字典和数组已经不一致了
            }
        }

        /// <summary>
        /// 根据ID号获得一个token，没有获得到就返回null
        /// </summary>
        /// <param name="id">Token的id</param>
        /// <returns>Token对象，没有则返回null</returns>
        public Token GetToken(int id)
        {
            //debug:这里尝试去掉加锁
            //lock (this._lockDict)
            //{
            if (_dictToken.ContainsKey(id)) {
                return _dictToken[id];
            }
            else {
                return null;
            }
            //}
        }

        /// <summary>
        /// 得到所有当前的Token。用来对所有Token进行遍历（仮）.
        /// 因为遍历操作频率较低，暂时目前所有对用户的处理都是通过这个来得到，将来可能需要改进。
        /// 加一个缓存数组.
        /// </summary>
        /// <returns>Token数组</returns>
        public Token[] GetAllToken()
        {
            if (this._dictToken.Count == 0) {
                return null;
            }
            if (_isDictEqualArr == false) //当前字典和数组已经不一致了
            {
                List<Token> listToken = new List<Token>();
                lock (this._lockDict) {
                    foreach (KeyValuePair<int, Token> kvp in _dictToken) {
                        listToken.Add(kvp.Value);
                    }
                    _arrToken = listToken.ToArray();

                    _isDictEqualArr = true; ////标记当前字典和数组已经一致了
                }
            }
            return _arrToken;
        }

        /// <summary>
        /// 向所有token发送数据。
        /// 在他们的消息队列中添加这个数据，但是不会开始一次发送。
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="index">数据的起始位置</param>
        /// <param name="length">数据的长度</param>
        public void SendToAllToken(byte[] data, int index, int length)
        {
            lock (this._lockDict) {
                foreach (KeyValuePair<int, Token> kvp in _dictToken) {
                    kvp.Value.AddSendData(data, index, length);
                }
            }
        }

        /// <summary>
        /// 向除了XX以外的所有token发送数据。
        /// 在他们的消息队列中添加这个数据，但是不会开始一次发送。
        /// </summary>
        /// <param name="exceptTokenID">排除的用户ID</param>
        /// <param name="data">数据</param>
        /// <param name="index">数据起始位置</param>
        /// <param name="length">数据长度</param>
        public void SendToAllTokenExcept(int exceptTokenID, byte[] data, int index, int length)
        {
            lock (this._lockDict) {
                foreach (KeyValuePair<int, Token> kvp in _dictToken) {
                    if (kvp.Value.ID != exceptTokenID) {
                        kvp.Value.AddSendData(data, index, length);
                    }
                }
            }
        }

        /// <summary>
        /// 未实现这个Clear()方法，但是存在Delete相关方法。
        /// </summary>
        internal void Clear()
        {
            _dictToken.Clear();
            _arrToken = null;
        }

        #endregion Exposed Function

        #region IDisposable implementation

        /// <summary>
        /// 这个释放其实不会用到
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            DxDebug.LogConsole(String.Format("TokenManager.Dispose()：进入了Dispose！"));
            if (disposed) {
                return;
            }
            if (disposing) {
                // 清理托管资源
                EventAddToken = null;
                EventDeleteToken = null;

                //断开所有
                DeleteAllToken();

                _dictToken.Clear();
                _dictToken = null;
                _arrToken = null;
            }
            // 清理非托管资源

            //让类型知道自己已经被释放
            disposed = true;
        }

        #endregion IDisposable implementation
    }
}
