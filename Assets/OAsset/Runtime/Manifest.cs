using System;
using System.Collections.Generic;
using UnityEngine;

namespace OAsset
{
    [Serializable]
    public class AssetInfo
    {
        public string AssetPath;
        public int BundleID;
    }

    [Serializable]
    public class BundleInfo
    {
        public string BundleName;
        public uint UnityCRC;
        public string FileHash;
        public uint FileCRC;
        public long FileSize;
        public int[] DependBundleIDs;
    }

    [Serializable]
    public class Manifest
    {
        public List<AssetInfo> AssetList = new List<AssetInfo>();
        public List<BundleInfo> BundleList = new List<BundleInfo>();

        private Dictionary<string, AssetInfo> _assetLookup;

        public void BuildLookup()
        {
            _assetLookup = new Dictionary<string, AssetInfo>(AssetList.Count);
            foreach (var asset in AssetList)
                _assetLookup[asset.AssetPath] = asset;
        }

        public bool TryGetAsset(string assetPath, out AssetInfo info)
        {
            if (_assetLookup == null)
                BuildLookup();
            return _assetLookup.TryGetValue(assetPath, out info);
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }

        public static Manifest FromJson(string json)
        {
            var manifest = JsonUtility.FromJson<Manifest>(json);
            manifest.BuildLookup();
            return manifest;
        }
    }
}
