namespace DNET
{
    /// <summary>
    /// 传递给工作线程的消息接口
    /// </summary>
    public interface IWorkMsg
    {
        /// <summary>
        /// 这个消息的名字。通常会用于追踪异常
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// 发出这个消息的源对象(通常是ILogic接口的逻辑对象)
        /// </summary>
        object srcObj { get; }

        /// <summary>
        /// 执行方法,在线程开始处理时调用，如果需要执行结束事件，可以在这个函数的最后自行实现
        /// </summary>
        void DoWork();
    }
}
