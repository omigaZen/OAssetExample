using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;

namespace OAsset
{
    public static class FileDownloader
    {
        private const int MaxRetries = 3;
        private const int CrcBufferSize = 8192;

        private static readonly uint[] _crc32Table;

        static FileDownloader()
        {
            _crc32Table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                    crc = (crc >> 1) ^ (0xEDB88320 & ~((crc & 1) - 1));
                _crc32Table[i] = crc;
            }
        }

        private static async UniTask<T> RetryAsync<T>(Func<UniTask<T>> action, string description)
        {
            Exception lastError = null;
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                if (attempt > 0)
                    await UniTask.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                try
                {
                    return await action();
                }
                catch (Exception e)
                {
                    lastError = e;
                    Debug.LogWarning($"[OAsset] {description} attempt {attempt + 1}/{MaxRetries} failed: {e.Message}");
                }
            }

            throw new Exception($"[OAsset] {description} failed after {MaxRetries} attempts", lastError);
        }

        private static async UniTask RetryAsync(Func<UniTask> action, string description)
        {
            Exception lastError = null;
            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                if (attempt > 0)
                    await UniTask.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                try
                {
                    await action();
                    return;
                }
                catch (Exception e)
                {
                    lastError = e;
                    Debug.LogWarning($"[OAsset] {description} attempt {attempt + 1}/{MaxRetries} failed: {e.Message}");
                }
            }

            throw new Exception($"[OAsset] {description} failed after {MaxRetries} attempts", lastError);
        }

        public static async UniTask DownloadFile(string url, string localPath, int timeoutSeconds,
            Action<float> onProgress = null)
        {
            string dir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await RetryAsync(async () =>
            {
                using var request = UnityWebRequest.Get(url);
                request.timeout = timeoutSeconds;
                request.downloadHandler = new DownloadHandlerFile(localPath) { removeFileOnAbort = true };

                var op = request.SendWebRequest();
                while (!op.isDone)
                {
                    onProgress?.Invoke(op.progress);
                    await UniTask.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                    throw new Exception($"Download failed: {request.error} ({url})");

                onProgress?.Invoke(1f);
            }, $"Download {url}");
        }

        /// <summary>
        /// 下载 Bundle 到临时文件，校验后写入 MD5 缓存目录
        /// </summary>
        public static async UniTask DownloadBundle(BundleInfo bundle, string cacheRoot, string serverUrl,
            int timeoutSeconds, Action<float> onProgress = null)
        {
            string url = serverUrl.TrimEnd('/') + "/" + bundle.BundleName;
            string tmpPath = Path.Combine(cacheRoot, bundle.BundleName + ".tmp");

            await DownloadFile(url, tmpPath, timeoutSeconds, onProgress);

            // 校验文件大小
            var fileInfo = new FileInfo(tmpPath);
            if (fileInfo.Length != bundle.FileSize)
            {
                File.Delete(tmpPath);
                throw new Exception(
                    $"[OAsset] Size mismatch for {bundle.BundleName}: expected {bundle.FileSize}, got {fileInfo.Length}");
            }

            // CRC32 验证
            uint crc = ComputeFileCRC32(tmpPath);
            if (crc != bundle.FileCRC)
            {
                File.Delete(tmpPath);
                throw new Exception(
                    $"[OAsset] CRC mismatch for {bundle.BundleName}: expected {bundle.FileCRC}, got {crc}");
            }

            // 校验通过，写入缓存目录
            WriteBundleToCache(tmpPath, bundle, cacheRoot);
        }

        /// <summary>
        /// 将 Bundle 数据写入 MD5 缓存结构：BundleFiles/{MD5前2位}/{MD5}/__data + __info
        /// </summary>
        public static void WriteBundleToCache(string sourcePath, BundleInfo bundle, string cacheRoot)
        {
            string bundleGUID = bundle.FileHash;
            string targetDir = GetBundleCachePath(cacheRoot, bundleGUID);
            Directory.CreateDirectory(targetDir);

            string dataPath = Path.Combine(targetDir, "__data");
            string infoPath = Path.Combine(targetDir, "__info");

            // 移动/复制数据文件
            if (File.Exists(dataPath))
                File.Delete(dataPath);
            File.Move(sourcePath, dataPath);

            // 写入校验信息
            File.WriteAllText(infoPath, $"{bundle.FileCRC},{bundle.FileSize}");
        }

        /// <summary>
        /// 将 byte[] 数据写入 MD5 缓存结构
        /// </summary>
        public static void WriteBundleToCache(byte[] data, BundleInfo bundle, string cacheRoot)
        {
            string bundleGUID = bundle.FileHash;
            string targetDir = GetBundleCachePath(cacheRoot, bundleGUID);
            Directory.CreateDirectory(targetDir);

            File.WriteAllBytes(Path.Combine(targetDir, "__data"), data);
            File.WriteAllText(Path.Combine(targetDir, "__info"), $"{bundle.FileCRC},{bundle.FileSize}");
        }

        /// <summary>
        /// 获取 Bundle 缓存路径：BundleFiles/{MD5前2位}/{MD5}/
        /// </summary>
        public static string GetBundleCachePath(string cacheRoot, string fileHash)
        {
            return Path.Combine(cacheRoot, "BundleFiles", fileHash.Substring(0, 2), fileHash);
        }

        /// <summary>
        /// 检查缓存中是否存在指定 Bundle
        /// </summary>
        public static bool IsBundleCached(string cacheRoot, string fileHash)
        {
            string dataPath = Path.Combine(GetBundleCachePath(cacheRoot, fileHash), "__data");
            return File.Exists(dataPath);
        }

        public static async UniTask<string> DownloadText(string url, int timeoutSeconds)
        {
            return await RetryAsync(async () =>
            {
                using var request = UnityWebRequest.Get(url);
                request.timeout = timeoutSeconds;
                await request.SendWebRequest().ToUniTask();

                if (request.result != UnityWebRequest.Result.Success)
                    throw new Exception($"Download failed: {request.error} ({url})");

                return request.downloadHandler.text;
            }, $"Download text {url}");
        }

        public static async UniTask<byte[]> DownloadBytes(string url, int timeoutSeconds)
        {
            return await RetryAsync(async () =>
            {
                using var request = UnityWebRequest.Get(url);
                request.timeout = timeoutSeconds;
                await request.SendWebRequest().ToUniTask();

                if (request.result != UnityWebRequest.Result.Success)
                    throw new Exception($"Download failed: {request.error} ({url})");

                return request.downloadHandler.data;
            }, $"Download bytes {url}");
        }

        public static uint ComputeFileCRC32(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return ComputeCRC32(stream);
        }

        public static uint ComputeCRC32(Stream stream)
        {
            uint crc = 0xFFFFFFFF;
            byte[] buffer = new byte[CrcBufferSize];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                    crc = (crc >> 8) ^ _crc32Table[(crc ^ buffer[i]) & 0xFF];
            }

            return ~crc;
        }

        public static uint ComputeCRC32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
                crc = (crc >> 8) ^ _crc32Table[(crc ^ b) & 0xFF];
            return ~crc;
        }
    }
}
