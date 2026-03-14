using System.IO;

namespace OAsset.Editor
{
    public class DefaultStrategy : IAssetBundleStrategy
    {
        private static readonly string[] ScanDirs = { "Assets/ToAb" };

        private static readonly string[] Filters =
        {
            ".prefab", ".png", ".jpg", ".jpeg", ".tga", ".bmp",
            ".mat", ".anim", ".controller", ".asset", ".unity",
            ".bytes", ".txt", ".json", ".wav", ".mp3", ".ogg",
            ".shader", ".compute", ".ttf", ".otf", ".fontsettings",
            ".mesh", ".fbx", ".obj"
        };

        public string GetBundleName(string assetPath)
        {
            // 去掉扩展名，转小写，/ 替换为 _
            string name = Path.ChangeExtension(assetPath, null);
            name = name.ToLowerInvariant().Replace("/", "_").Replace("\\", "_");
            return name + ".bundle";
        }

        public string[] GetScanDirectories()
        {
            return ScanDirs;
        }

        public string[] GetAssetFilters()
        {
            return Filters;
        }
    }
}
