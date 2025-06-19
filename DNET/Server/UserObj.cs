using System.Collections.Generic;

namespace DNET
{
    /// <summary>
    /// 绑定Token的用户自定义对象,使用的时候要及时清除
    /// </summary>
    public class UserObj
    {
        /// <summary>
        /// 对象字典
        /// </summary>
        private Dictionary<int, object> _dict = null;

        /// <summary>
        /// 可以直接使用，省的使用字典
        /// </summary>
        public object obj = null;

        /// <summary>
        /// 可以直接使用，省的使用字典
        /// </summary>
        public object obj2 = null;

        /// <summary>
        /// 可以直接使用，省的使用字典
        /// </summary>
        public object obj3 = null;

        /// <summary>
        /// 得到一个记录的对象
        /// </summary>
        /// <param name="key">对象的key，可以使用common协议中的type</param>
        /// <returns>如果不存在则返回null</returns>
        public object Get(int key)
        {
            if (_dict == null) {
                return null;
            }
            if (_dict.ContainsKey(key)) {
                return _dict[key];
            }
            else {
                return null;
            }
        }

        /// <summary>
        /// 得到一个记录的对象，没有做判断和安全处理
        /// </summary>
        /// <param name="key">对象的key，可以使用common协议中的type</param>
        /// <param name="obj">要加入的obj</param>
        /// <returns>成功添加返回true，重复返回false</returns>
        public bool Add(int key, object obj)
        {
            if (_dict == null) {
                _dict = new Dictionary<int, object>();
            }
            if (!_dict.ContainsKey(key)) //如果没有存在那么就添加这个
            {
                _dict.Add(key, obj);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 重设一个key的值
        /// </summary>
        /// <param name="key">对象的key，可以使用common协议中的type</param>
        /// <param name="obj">要重设的obj</param>
        /// <returns>成功 重设返回true，重复返回false</returns>
        public bool Set(int key, object obj)
        {
            if (_dict == null) {
                return false;
            }
            if (_dict.ContainsKey(key)) //如果没有存在那么就添加这个
            {
                _dict[key] = null;
                _dict[key] = obj;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 删除一个键值
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool Delete(int key)
        {
            if (_dict == null) {
                return false;
            }
            if (_dict.ContainsKey(key)) //如果没有存在那么就添加这个
            {
                _dict[key] = null;
                _dict.Remove(key);
                return true;
            }
            return false;
        }
    }
}
