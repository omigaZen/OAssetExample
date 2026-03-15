using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace OAsset.Editor
{
    public static class OAssetBuilder
    {
        private const string OutputDir = "Assets/StreamingAssets/AB";
        private const string VersionFileName = "version.txt";

        [MenuItem("OAsset/Build (Default Strategy)", false, 100)]
        public static void Build()
        {
            BuildInternal(false);
        }

        [MenuItem("OAsset/Build (Force Rebuild)", false, 101)]
        public static void ForceRebuild()
        {
            BuildInternal(true);
        }

        [MenuItem("OAsset/Apply Bundle Names", false, 200)]
        public static void ApplyBundleNames()
        {
            var strategy = new DefaultStrategy();
            ApplyBundleNamesInternal(strategy);
            Debug.Log("[OAsset] Bundle names applied.");
        }

        [MenuItem("OAsset/Clear All Bundle Names", false, 201)]
        public static void ClearBundleNames()
        {
            foreach (string name in AssetDatabase.GetAllAssetBundleNames())
                AssetDatabase.RemoveAssetBundleName(name, true);
            Debug.Log("[OAsset] All bundle names cleared.");
        }

        [MenuItem("OAsset/Show Bundle Info", false, 300)]
        public static void ShowInfo()
        {
            string[] names = AssetDatabase.GetAllAssetBundleNames();
            Debug.Log($"[OAsset] {names.Length} asset bundles defined:");
            foreach (string name in names)
            {
                string[] assets = AssetDatabase.GetAssetPathsFromAssetBundle(name);
                Debug.Log($"  {name}: {assets.Length} assets");
                foreach (string asset in assets)
                    Debug.Log($"    - {asset}");
            }
        }

        private static void BuildInternal(bool forceRebuild)
        {
            var strategy = new DefaultStrategy();

            // 1. 扫描并设置 Bundle 名称
            var assetPaths = ScanAssets(strategy);
            if (assetPaths.Count == 0)
            {
                Debug.LogWarning("[OAsset] No assets found to build.");
                return;
            }

            // 2. 清理旧的 Bundle 名称（可选）并应用新名称
            ApplyBundleNamesInternal(strategy);

            // 3. 清除并重建输出目录
            if (Directory.Exists(OutputDir))
                Directory.Delete(OutputDir, true);
            Directory.CreateDirectory(OutputDir);

            // 4. 构建 AssetBundles (LZ4)
            var options = BuildAssetBundleOptions.ChunkBasedCompression;
            if (forceRebuild)
                options |= BuildAssetBundleOptions.ForceRebuildAssetBundle;

            var buildManifest = BuildPipeline.BuildAssetBundles(
                OutputDir, options, EditorUserBuildSettings.activeBuildTarget);

            if (buildManifest == null)
            {
                Debug.LogError("[OAsset] Build failed.");
                return;
            }

            // 5. 生成 version.txt
            string version = DateTime.Now.ToString("yyyy.MM.dd.HHmmss");
            string versionPath = Path.Combine(OutputDir, VersionFileName);
            File.WriteAllText(versionPath, version);

            // 6. 生成 <版本号>.manifest.json
            var manifest = GenerateManifest(strategy, assetPaths, buildManifest);
            string manifestJson = manifest.ToJson();
            string manifestFileName = $"{version}.manifest.json";
            string manifestPath = Path.Combine(OutputDir, manifestFileName);
            File.WriteAllText(manifestPath, manifestJson);

            // 7. 清理 Unity 生成的多余文件
            CleanupUnityManifests();

            AssetDatabase.Refresh();
            Debug.Log($"[OAsset] Build complete. Version: {version}, " +
                      $"{manifest.AssetList.Count} assets, {manifest.BundleList.Count} bundles.");
        }

        private static List<string> ScanAssets(IAssetBundleStrategy strategy)
        {
            var result = new List<string>();
            var filters = new HashSet<string>(strategy.GetAssetFilters(), StringComparer.OrdinalIgnoreCase);

            foreach (string dir in strategy.GetScanDirectories())
            {
                if (!Directory.Exists(dir))
                {
                    Debug.LogWarning($"[OAsset] Scan directory not found: {dir}");
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets("", new[] { dir });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path))
                        continue;

                    string ext = Path.GetExtension(path);
                    if (filters.Contains(ext))
                        result.Add(path);
                }
            }

            return result.Distinct().ToList();
        }

        private static void ApplyBundleNamesInternal(IAssetBundleStrategy strategy)
        {
            var assetPaths = ScanAssets(strategy);
            foreach (string path in assetPaths)
            {
                var importer = AssetImporter.GetAtPath(path);
                if (importer == null) continue;

                string bundleName = strategy.GetBundleName(path);
                if (importer.assetBundleName != bundleName)
                {
                    importer.assetBundleName = bundleName;
                    importer.SaveAndReimport();
                }
            }

            AssetDatabase.RemoveUnusedAssetBundleNames();
        }

        private static Manifest GenerateManifest(IAssetBundleStrategy strategy,
            List<string> assetPaths, AssetBundleManifest buildManifest)
        {
            var manifest = new Manifest();
            string[] allBundles = buildManifest.GetAllAssetBundles();

            // 建立 bundleName -> index 映射
            var bundleIndexMap = new Dictionary<string, int>(allBundles.Length);
            for (int i = 0; i < allBundles.Length; i++)
            {
                bundleIndexMap[allBundles[i]] = i;

                string bundlePath = Path.Combine(OutputDir, allBundles[i]);
                var bundleInfo = new BundleInfo
                {
                    BundleName = allBundles[i],
                };

                // 文件信息
                if (File.Exists(bundlePath))
                {
                    var fileInfo = new FileInfo(bundlePath);
                    bundleInfo.FileSize = fileInfo.Length;
                    bundleInfo.FileCRC = FileDownloader.ComputeFileCRC32(bundlePath);
                    bundleInfo.FileHash = ComputeFileMD5(bundlePath);
                }

                // Unity CRC
                BuildPipeline.GetCRCForAssetBundle(bundlePath, out uint crc);
                bundleInfo.UnityCRC = crc;

                // 依赖
                string[] deps = buildManifest.GetDirectDependencies(allBundles[i]);
                var depIds = new List<int>(deps.Length);
                foreach (string d in deps)
                {
                    if (bundleIndexMap.TryGetValue(d, out int depIndex))
                        depIds.Add(depIndex);
                    else
                        Debug.LogWarning($"[OAsset] Dependency not found in build output: {d} (referenced by {allBundles[i]})");
                }
                bundleInfo.DependBundleIDs = depIds.ToArray();

                manifest.BundleList.Add(bundleInfo);
            }

            // 资源列表
            foreach (string path in assetPaths)
            {
                string bundleName = strategy.GetBundleName(path);
                if (!bundleIndexMap.TryGetValue(bundleName, out int bundleId))
                    continue;

                var assetInfo = new AssetInfo
                {
                    AssetPath = path,
                    BundleID = bundleId,
                };

                manifest.AssetList.Add(assetInfo);
            }

            return manifest;
        }

        private static string ComputeFileMD5(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static void CleanupUnityManifests()
        {
            // 删除 Unity 自动生成的 .manifest 文件
            string[] manifestFiles = Directory.GetFiles(OutputDir, "*.manifest");
            foreach (string f in manifestFiles)
                File.Delete(f);

            // 删除与输出目录同名的 bundle 文件（Unity 的主 manifest bundle）
            string dirBundleName = Path.GetFileName(OutputDir);
            string mainBundlePath = Path.Combine(OutputDir, dirBundleName);
            if (File.Exists(mainBundlePath))
                File.Delete(mainBundlePath);
        }
    }
}
