using UnityEngine;

namespace OAsset
{
    [CreateAssetMenu(fileName = "OAssetConfig", menuName = "Config/OAssetConfig")]
    public class OAssetConfig : ScriptableObject
    {
        [Header("服务器配置")]
        public string serverUrl;
        public int timeoutSeconds = 30;

        [Header("缓存配置")]
        public string cacheRootPath;

        public string GetCacheRoot()
        {
            if (!string.IsNullOrEmpty(cacheRootPath))
                return System.IO.Path.Combine(Application.persistentDataPath, cacheRootPath);
            return System.IO.Path.Combine(Application.persistentDataPath, "OAssetCache");
        }
    }
}
