# RuntimeLightmaps

把烘焙好的光照贴图从美术场景里摘出来，运行时挂到预制体上。插件不负责烘焙，只负责导出和运行时合并。

把本仓库里的 `Runtime`、`Editor` 以及这两个目录的 `.meta` 放进目标工程的 `Assets/RuntimeLightmaps`。不依赖 Addressables、Odin、XLua。运行时程序集 `RuntimeLightmaps` 会被自动引用。编辑器程序集只在编辑器编译，菜单是 `Tools/RuntimeLightmaps/Export Bundle From Selected Root`。

`.meta` 的 guid 不要改。已经挂上的 `LightmapPrefabBinding` 和已经导出的 `LightmapBundle` 靠这些 guid 认脚本。

加载脚本留在游戏工程里。拿到 `LightmapBundle` 后调用 `LightmapPrefabBinding.SetDefaultBundle`，或把资源拖到组件的 `bundle` 字段。导出的 Bundle 和它引用的光照贴图是工程数据，不要提交进这个插件仓库。

## 文件

| 路径 | 作用 |
| --- | --- |
| `Runtime/LightmapBundle.cs` | 导出资源。贴图数组加每个 Renderer 的路径、烘焙索引、`lightmapScaleOffset` |
| `Runtime/RuntimeLightmapRegistry.cs` | 把多份 Bundle 合并进 `LightmapSettings.lightmaps` |
| `Runtime/LightmapPrefabBinding.cs` | 挂在预制体根上，写回光照索引和缩放偏移 |
| `Editor/LightmapBundleExporter.cs` | 从当前已烘焙场景导出 Bundle |
| `Editor/LightmapEditorPreview.cs` | 编辑器里不进 Play 也套用，方便看 Scene 视图 |

## 序列化字段，不能改名

`LightmapBundle`：

- `colorLightmaps`
- `lightmapDirs`
- `bindings`
- `RendererBinding.path`
- `RendererBinding.bakedLightmapIndex`
- `RendererBinding.bakedScaleOffset`

`LightmapPrefabBinding`：

- `bundle`
- `applyOnAwake`
- `runtimeDebugLogs`
- `forceLightmapKeywords`

## 导出

1. 在美术场景里正常 Bake，场景里要能看到 `LightmapSettings.lightmaps`。
2. 在 Hierarchy 选中将来作为预制体根的物体。绑定路径相对这个根计算。
3. 菜单 `Tools/RuntimeLightmaps/Export Bundle From Selected Root`。
4. 默认保存到 `Assets/RuntimeLightmapBundles/<根节点名>_LightmapBundle.asset`。
5. 导出只记录贴图引用和 Renderer 绑定，不复制贴图文件。Bundle 和它引用的 lightmap、dir 贴图要一起进目标工程。

`lightmapIndex < 0` 的 Renderer 会跳过。一个都没有会中止。

## 运行时

预制体根挂 `LightmapPrefabBinding`。

- 组件上指定 `bundle`，勾 `applyOnAwake`，`Awake` 里会 `Apply()`。
- 不指定 `bundle` 时，外部加载完后调用 `LightmapPrefabBinding.SetDefaultBundle(bundle)`。

`Apply()` 只成功执行一次：合并进全局 `LightmapSettings.lightmaps`，再按相对路径写回 Renderer。`forceLightmapKeywords` 会给共享材质打开 `LIGHTMAP_ON` 和 `DIRLIGHTMAP_COMBINED`，这是 Built-in 管线的关键字。URP/HDRP 不要开。

## 发布出来是黑的

编辑器里正常、打包是黑的，先看 **Edit > Project Settings > Graphics > Shader Stripping > Lightmap Modes**。

必须是 **Custom**，下面六项全部勾上：Baked Non-Directional、Baked Directional、Realtime Non-Directional、Realtime Directional、Baked Shadowmask、Baked Subtractive。

Automatic 会裁掉只在运行时才用到的光照贴图变体，包里就是黑的。

## 网格和材质

- 网格要保留烘焙时那套 UV2。
- 预制体层级和名字要和导出时一致。
- 材质 Shader 要采样 lightmap。
- 颜色空间要和烘焙工程一致。
- 插件不写 shadowmask。
