using Cysharp.Threading.Tasks;
using OAsset;
using UnityEngine;

public class BootStrap : MonoBehaviour
{
    public OAssetConfig config;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Initialize().Forget();
    }

    private async UniTaskVoid Initialize()
    {
        var oasset = OAsset.OAsset.Instance;
        await oasset.InitializeAsync(config);
        var o = await oasset.LoadAssetAsync<GameObject>("Assets/ToAb/Triangle.prefab");
        Object.Instantiate(o, this.transform);
        
        // Application.persistentDataPath
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
