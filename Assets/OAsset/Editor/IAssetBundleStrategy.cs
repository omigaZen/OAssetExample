namespace OAsset.Editor
{
    public interface IAssetBundleStrategy
    {
        /// <summary>
        /// 根据资源路径生成 Bundle 名称
        /// </summary>
        string GetBundleName(string assetPath);

        /// <summary>
        /// 返回需要扫描的目录列表
        /// </summary>
        string[] GetScanDirectories();

        /// <summary>
        /// 返回资源文件过滤扩展名列表（含点号，如 ".prefab"）
        /// </summary>
        string[] GetAssetFilters();
    }
}
