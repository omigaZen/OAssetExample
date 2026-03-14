using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;

namespace OAsset
{
    public class OAsset
    {
        private static OAsset _instance;
        public static OAsset Instance => _instance ??= new OAsset();

        private OAssetConfig _config;
        private Manifest _manifest;
        private readonly Dictionary<string, AssetBundle> _loadedBundles = new();
        private readonly Dictionary<string, UniTask<AssetBundle>> _loadingBundles = new();
        private string _cacheRoot;
        private string _version;
        private bool _initialized;

        private const string VersionFileName = "version.txt";
        private const string StreamingAssetsSubDir = "AB";

        public bool IsInitialized => _initialized;

        public async UniTask InitiateAsync(OAssetConfig config)
        {
            if (_initialized)
            {
                Debug.LogWarning("[OAsset] Already initialized.");
                return;
            }

            _config = config;
            _cacheRoot = config.GetCacheRoot();

            string cachedVersionPath = Path.Combine(_cacheRoot, VersionFileName);
            bool isFirstLaunch = !Directory.Exists(_cacheRoot);

            if (isFirstLaunch)
            {
                // 首次启动：从 StreamingAssets 复制到缓存
                Debug.Log("[OAsset] First launch, copying from StreamingAssets...");
                await InitFromStreamingAssets();
            }
            else if (!string.IsNullOrEmpty(_config.serverUrl))
            {
                // 非首次：检查远程版本更新
                try
                {
                    string remoteVersionUrl = _config.serverUrl.TrimEnd('/') + "/" + VersionFileName;
                    string remoteVersion = await FileDownloader.DownloadText(remoteVersionUrl, _config.timeoutSeconds);
                    remoteVersion = remoteVersion.Trim();

                    string localVersion = File.Exists(cachedVersionPath)
                        ? File.ReadAllText(cachedVersionPath).Trim()
                        : "";

                    if (remoteVersion != localVersion)
                    {
                        Debug.Log($"[OAsset] Version changed: {localVersion} -> {remoteVersion}, updating manifest...");
                        string remoteManifestUrl = _config.serverUrl.TrimEnd('/') + "/" +
                                                   $"{remoteVersion}.manifest.json";
                        byte[] manifestData =
                            await FileDownloader.DownloadBytes(remoteManifestUrl, _config.timeoutSeconds);

                        // 写入新清单，更新版本号
                        string newManifestPath = Path.Combine(_cacheRoot, $"{remoteVersion}.manifest.json");
                        File.WriteAllBytes(newManifestPath, manifestData);
                        File.WriteAllText(cachedVersionPath, remoteVersion);
                    }
                    else
                    {
                        Debug.Log("[OAsset] Version up to date.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[OAsset] Version check failed, using cached manifest: {e.Message}");
                }
            }

            // 读取版本号，加载清单
            _version = File.Exists(cachedVersionPath)
                ? File.ReadAllText(cachedVersionPath).Trim()
                : "";

            string manifestPath = Path.Combine(_cacheRoot, $"{_version}.manifest.json");
            if (File.Exists(manifestPath))
            {
                string json = File.ReadAllText(manifestPath);
                _manifest = Manifest.FromJson(json);
                Debug.Log(
                    $"[OAsset] Manifest loaded: {_manifest.AssetList.Count} assets, {_manifest.BundleList.Count} bundles.");
            }
            else
            {
                _manifest = new Manifest();
                Debug.LogWarning("[OAsset] No manifest found, initialized with empty manifest.");
            }

            _initialized = true;
        }

        public async UniTask<T> LoadAssetAsync<T>(string assetPath) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            // 编辑器模式：直接使用 AssetDatabase
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
                return asset;
            Debug.LogWarning($"[OAsset] Editor: asset not found at {assetPath}, falling back to bundle.");
#endif
            if (!_initialized)
                throw new InvalidOperationException("[OAsset] Not initialized. Call InitiateAsync first.");

            if (!_manifest.TryGetAsset(assetPath, out var assetInfo))
                throw new FileNotFoundException($"[OAsset] Asset not found in manifest: {assetPath}");

            // 先加载依赖的 AB 包
            var bundleInfo = _manifest.BundleList[assetInfo.BundleID];
            if (assetInfo.DependBundleIDs != null)
            {
                foreach (int depId in assetInfo.DependBundleIDs)
                    await EnsureBundleLoaded(_manifest.BundleList[depId]);
            }

            // 加载主 Bundle
            await EnsureBundleLoaded(bundleInfo);

            // 从 Bundle 中加载资源
            var bundle = _loadedBundles[bundleInfo.FileHash];
            var request = bundle.LoadAssetAsync<T>(assetPath);
            await request.ToUniTask();
            return request.asset as T;
        }

        public void UnloadBundle(string fileHash, bool unloadAllObjects = false)
        {
            if (_loadedBundles.TryGetValue(fileHash, out var bundle))
            {
                bundle.Unload(unloadAllObjects);
                _loadedBundles.Remove(fileHash);
            }
        }

        public void UnloadAllBundles(bool unloadAllObjects = true)
        {
            foreach (var kvp in _loadedBundles)
                kvp.Value.Unload(unloadAllObjects);
            _loadedBundles.Clear();
            _loadingBundles.Clear();
        }

        private async UniTask EnsureBundleLoaded(BundleInfo bundleInfo)
        {
            string bundleMD5 = bundleInfo.FileHash;

            // 已加载则直接返回
            if (_loadedBundles.ContainsKey(bundleMD5))
                return;

            // 防止重复加载：如果正在加载中，等待现有任务
            if (_loadingBundles.TryGetValue(bundleMD5, out var loadingTask))
            {
                await loadingTask;
                return;
            }

            var task = LoadBundleInternal(bundleInfo);
            _loadingBundles[bundleMD5] = task;

            try
            {
                await task;
            }
            finally
            {
                _loadingBundles.Remove(bundleMD5);
            }
        }

        private async UniTask<AssetBundle> LoadBundleInternal(BundleInfo bundleInfo)
        {
            string bundleMD5 = bundleInfo.FileHash;

            // 检查本地缓存是否存在
            if (!FileDownloader.IsBundleCached(_cacheRoot, bundleMD5))
            {
                // 缓存不存在，从服务器下载
                if (string.IsNullOrEmpty(_config.serverUrl))
                    throw new FileNotFoundException(
                        $"[OAsset] Bundle not found and no server configured: {bundleInfo.BundleName}");

                await FileDownloader.DownloadBundle(bundleInfo, _cacheRoot, _config.serverUrl,
                    _config.timeoutSeconds);
            }

            // 从缓存加载 AssetBundle
            string dataPath = Path.Combine(
                FileDownloader.GetBundleCachePath(_cacheRoot, bundleMD5), "__data");

            var loadRequest = AssetBundle.LoadFromFileAsync(dataPath);
            await loadRequest.ToUniTask();

            if (loadRequest.assetBundle == null)
            {
                // 加载失败，清除损坏的缓存
                string cacheDir = FileDownloader.GetBundleCachePath(_cacheRoot, bundleMD5);
                if (Directory.Exists(cacheDir))
                    Directory.Delete(cacheDir, true);
                throw new Exception($"[OAsset] Failed to load bundle: {bundleInfo.BundleName}");
            }

            _loadedBundles[bundleMD5] = loadRequest.assetBundle;
            return loadRequest.assetBundle;
        }

        /// <summary>
        /// 首次启动：从 StreamingAssets 复制所有资源到缓存目录
        /// </summary>
        private async UniTask InitFromStreamingAssets()
        {
            string streamingPath = Path.Combine(Application.streamingAssetsPath, StreamingAssetsSubDir);

            // 1. 创建缓存目录
            Directory.CreateDirectory(Path.Combine(_cacheRoot, "BundleFiles"));

            // 2. 读取内置版本号
            string versionUrl = GetStreamingAssetsUrl(VersionFileName);
            string version = await ReadStreamingFile(versionUrl);
            version = version.Trim();

            // 3. 读取清单文件
            string manifestFileName = $"{version}.manifest.json";
            string manifestUrl = GetStreamingAssetsUrl(manifestFileName);
            string manifestJson = await ReadStreamingFile(manifestUrl);
            var manifest = Manifest.FromJson(manifestJson);

            // 4. 复制每个 Bundle 到缓存目录
            for (int i = 0; i < manifest.BundleList.Count; i++)
            {
                var bundle = manifest.BundleList[i];
                Debug.Log($"[OAsset] Copying bundle {i + 1}/{manifest.BundleList.Count}: {bundle.BundleName}");

                string bundleUrl = GetStreamingAssetsUrl(bundle.BundleName);
                byte[] bundleData = await ReadStreamingFileBytes(bundleUrl);

                FileDownloader.WriteBundleToCache(bundleData, bundle, _cacheRoot);
            }

            // 5. 复制版本文件和清单文件
            File.WriteAllText(Path.Combine(_cacheRoot, VersionFileName), version);
            File.WriteAllText(Path.Combine(_cacheRoot, manifestFileName), manifestJson);

            Debug.Log($"[OAsset] First launch init complete. Version: {version}, " +
                      $"{manifest.BundleList.Count} bundles copied.");
        }

        private static string GetStreamingAssetsUrl(string fileName)
        {
            string basePath = Path.Combine(Application.streamingAssetsPath, StreamingAssetsSubDir);
#if UNITY_ANDROID && !UNITY_EDITOR
            return basePath + "/" + fileName; // jar:file:// URL
#else
            return Path.Combine(basePath, fileName);
#endif
        }

        private static async UniTask<string> ReadStreamingFile(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var request = UnityWebRequest.Get(path);
            await request.SendWebRequest().ToUniTask();
            if (request.result != UnityWebRequest.Result.Success)
                throw new Exception($"[OAsset] Failed to read StreamingAssets: {request.error} ({path})");
            return request.downloadHandler.text;
#else
            if (!File.Exists(path))
                throw new FileNotFoundException($"[OAsset] StreamingAssets file not found: {path}");
            return File.ReadAllText(path);
#endif
        }

        private static async UniTask<byte[]> ReadStreamingFileBytes(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var request = UnityWebRequest.Get(path);
            await request.SendWebRequest().ToUniTask();
            if (request.result != UnityWebRequest.Result.Success)
                throw new Exception($"[OAsset] Failed to read StreamingAssets: {request.error} ({path})");
            return request.downloadHandler.data;
#else
            if (!File.Exists(path))
                throw new FileNotFoundException($"[OAsset] StreamingAssets file not found: {path}");
            return File.ReadAllBytes(path);
#endif
        }
    }
}
