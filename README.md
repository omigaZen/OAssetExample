# OAsset

Unity AssetBundle 资源管理系统 - 精简版 YooAsset

## 特性

- 简单易用的 AssetBundle 打包与加载
- 自动依赖管理
- 支持热更新
- 基于文件路径的寻址方式
- 内置缓存机制
- UniTask 异步支持

## 快速开始

### 安装

1. 将 `Assets/OAsset` 目录复制到你的 Unity 项目中
2. 确保项目已安装 [UniTask](https://github.com/Cysharp/UniTask)

### 打包资源

1. 将需要打包的资源放入 `Assets/ToAB` 目录
2. 菜单栏选择 `OAsset > Build (Default Strategy)` 执行打包
3. 打包结果输出到 `Assets/StreamingAssets/AB/`

### 运行时加载

```csharp
using OAsset;

// 初始化
var config = Resources.Load<OAssetConfig>("OAssetConfig");
await OAsset.Instance.InitiateAsync(config);

// 加载资源
var prefab = await OAsset.Instance.LoadAssetAsync<GameObject>("Assets/ToAB/Player.prefab");
Instantiate(prefab);
```

## 模块说明

| 模块 | 说明 |
|------|------|
| OAsset | 运行时资源加载、缓存、卸载 |
| OAssetBuilder | 编辑器 AssetBundle 打包 |
| OAssetConfig | 配置管理 |
| FileDownloader | 文件下载 |

## 配置项

| 配置项 | 说明 |
|--------|------|
| serverUrl | 资源服务器地址 |
| timeoutSeconds | 请求超时时间 |
| cacheRootPath | 缓存根目录 |
| maxCacheSizeMB | 最大缓存大小 |

## 打包菜单

| 菜单项 | 功能 |
|--------|------|
| OAsset > Build (Default Strategy) | 使用默认策略打包 |
| OAsset > Build (Force Rebuild) | 强制重建所有 Bundle |
| OAsset > Clear All Bundle Names | 清除所有 Bundle 名称标记 |

## 与 YooAsset 的区别

| 简化项 | 说明 |
|--------|------|
| Package 概念 | 移除，仅需单一 Package |
| 寻址名 | 移除，直接用文件路径寻址 |
| 功能精简 | 仅保留核心功能 |

## 许可证

MIT License
