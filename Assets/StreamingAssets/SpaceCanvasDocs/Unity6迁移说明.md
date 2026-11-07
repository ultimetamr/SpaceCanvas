# Unity 6 迁移记录

迁移目标：Unity 6.6 / 6000.6.8f1。工程路径仍为 `D:\Would\DOME\SpaceCanvas`。

## 打开工程

在 Unity Hub 中为本工程选择 6000.6.8f1；也可双击工程根目录的 `Open-SpaceCanvas-Unity6.cmd`。启动场景仍为 `Assets/Scenes/SpaceCanvas_FullSpaceMR.unity`，平台仍为 Android。

## 迁移内容

- 由 Unity 6 包管理器升级 URP、UGUI/TMP、Input System、OpenXR、XRI 及测试框架，并保存 manifest 与 lock 文件。
- PICO XR 保留项目内 6.1.1 SDK，运行方式保留 Full Space MR / OpenXR。
- 按 SDK 的透视功能校验要求引入 `com.unity.xr.compositionlayers`。
- 官方 TMP 必需资源已升级，并恢复工程原有字体与回退配置。
- `SpaceCanvasUnityCompatibility` 统一处理文字换行以及交互会话 ID；Unity 6.6 的 64 位 EntityId 不截断为 int。工程 JSON 的持久对象 ID 不变。
- 嵌入式 SpatialAdapter 的编辑器导出器增加 Unity 6.6 EntityId / SerializedProperty 兼容分支。更新或替换该嵌入包时需重新核对这部分改动。
- 修正嵌入式 PICO 企业虚拟屏示例在新版 Composition Layers 中的 layerId 获取方式，采用官方管理器分配的 layerId。
- 修正材质比较测试的浮点容差，以及 Unity 6 EditMode 不允许 SendMessage 派发 Awake 的测试调用方式。

## 备份与验证

2022 源工程升级前备份：`E:\SpaceCanvas-Unity2022-Backup-20261106-140924`。备份保留 Assets、Packages、ProjectSettings、存档和构建产物；Library / Temp / obj 为可再生目录，未复制。

不要用 Unity 2022 直接打开已迁移的源工程。若需回退，请先把上述备份复制到独立目录，再用 2022 打开。

迁移后的编译成功不代表真机已验证。Unity 6 改变了渲染、文字、OpenXR 和 Android 工具链，重新打包后仍需在 Swan 检查透视、射线点击、手势和导入导出。

Unity 6 EditMode 回归已通过 124 项，0 失败、0 跳过。机器同时打开多个 Unity 工程曾出现内存不足和原生导入崩溃；验证阶段降低了导入并发。升级日志位于工程 `Logs/Unity6-*.log`，NUnit 报告为 `Logs/Unity6-EditMode.Results.xml`。

## 2026-11-06 Android 构建与放置交互修复

- OpenXR 1.18 按插件路径匹配自定义 loader 名称。嵌入式 PICO SDK 的 `CustomRuntimeLoaderName` 与现有 `LoaderForUnitySDK.aar` 保持一致，修复 Android 构建找不到 loader 的问题。
- Unity 6 Windows 构建期间将进程 TEMP/TMP 临时指向 `Temp/SpaceCanvasJava`，构建后恢复，规避 JDK 在 DOS 短路径下无法创建 Gradle IPC 连接的问题；不修改系统环境变量。
- 放置预览使用持久化的 URP Unlit 透明材质 `Assets/Resources/SpaceCanvasMaterials/PlacementPreview.mat`，保留 Android 所需透明变体。预览保留素材颜色与纹理，透明度 0.38，不写深度、不投射阴影、不参与碰撞与工程导出；取消或确认后释放临时材质。
- 输入源选择校验独立 aim 追踪状态与有限姿态，射线丢失时禁止退回手腕的重置姿态。优先原生 OpenXR 输入配置，保留合成手部输入作为可用回退。仍要求选素材后独立第二次按下才确认放置。
- 最新完整 EditMode 回归：127 通过、0 失败、0 跳过，报告 `Logs/Unity6-Placement-EditMode.Results.xml`。包括透明预览、确认位置一致、单次生成与撤销、手腕有效但 aim 丢失，以及重复手部输入源选择。
- APK 构建成功，0 错误、69 警告；签名验证通过。产物 `Builds/SpaceCanvas-FullSpaceMR.apk`，SHA256 `3398B50A9227947EFB43C095268AC00D2E1B526F38D33828DDF937820D482EFD`。版本号仍为 0.3.2 / code 5，通过哈希区分本次包。
- 已覆盖安装至 Swan 真机，用户确认“半透明正常，放置位置也正常”。运行日志位于 `Logs/Unity6-Placement-Fix-Startup.json`。这项确认覆盖本次放置交互，不等同于完整性能、眼动或全部功能验收。
- 覆盖安装前存档备份：`DeviceProjects/PB314XHGKC160016G/Unity6-BeforePlacementFix/Projects/`，旧 APK 保存在 `Builds/Unity6-BeforePlacementFix/`。
