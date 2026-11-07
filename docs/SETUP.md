# 从公开源码恢复开发环境

## 1. 安装工具

在 Unity Hub 安装 **6000.6.8f1** 及 Android Build Support（SDK、NDK、OpenJDK），安装 Git 后克隆仓库。本项目已经迁移到 Unity 6，不建议直接用 Unity 2022 打开当前分支。

## 2. 恢复两个受限 SDK

公开仓库不包含这两个本地包，因为它们自带的许可证禁止未经书面许可再分发。请通过 PICO 官方开发者渠道或已有授权安装材料自行获取：

- PICO Unity SDK **6.1.1**，本项目使用包目录名 `PICO-Unity-SDK-6.1.1-20260903`，package name 为 `com.bytedance.pico.xr`。
- PICO SpatialAdapter **6.1.0**，包目录名 `com.bytedance.pico.spatialadapter`。

Windows 上，关闭 Unity，在仓库根目录打开 PowerShell，执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Restore-PicoDependencies.ps1 -SdkPath 'D:\SDK\PICO-Unity-SDK-6.1.1-20260903' -SpatialAdapterPath 'D:\SDK\com.bytedance.pico.spatialadapter'
```

参数指向包含 `package.json` 的解压目录。脚本只复制到尚不存在的目标目录，不删除已有内容；然后应用本项目需要的 Unity 6 兼容调整。若已手动放好 SDK，直接不带这两个参数运行脚本即可。完成后再打开 Unity；其余 registry 依赖由 UPM 下载。

脚本针对本项目包版本，已修复的文件会跳过。结构不匹配会报出文件路径和原因，先核对 SDK 版本，不应继续强行编译。不同版本 SDK 需要重新评估，而不是随意替换包名。

## 3. 兼容处理具体内容

- PICO `Runtime/PICOFeature.cs` 的 `CustomRuntimeLoaderName` 与实际 `LoaderForUnitySDK.aar` 对齐。
- PICO 企业虚拟屏示例在 Unity 6.6 使用 `CompositionLayerManager.TryGetLayerId`，避免把 EntityId 传给 int layerId。
- SpatialAdapter 编辑器导出器使用自有的 `SpatialAdapterUnity6Ids` 会话令牌映射，适配 Unity 6.6 的 EntityId。
- SpatialAdapter `SerializedProperty` 引用检查和清理改用 Unity 6.6 的 `objectReferenceEntityIdValue`，保留旧版分支。

这些调整由仓库中的恢复脚本局部应用；SDK 本体仍由接入者自行提供。SDK 更新后请核对这些改动是否已被官方修复。

## 4. 场景与构建

打开 `Assets/Scenes/SpaceCanvas_FullSpaceMR.unity`。这是现有 MR 工作台入口。Android Build Profile 采用此场景，应用包名 `com.spacecanvas.prototype`，版本 `0.3.2` / code `5`。

仓库中的 `Open-SpaceCanvas-Unity6.cmd` 记录原开发电脑的 Unity 安装位置；其他电脑优先通过 Unity Hub 打开，或自行修改该启动脚本。构建入口为 `SpaceCanvas.ProjectIO.Editor.XR.SpaceCanvasFullSpaceBuilder.BuildExistingAndroid`。使用现有场景构建可保留已导入、移动和缩放的布局；不要执行重新生成默认场景的入口替代它。

需查看开发菜单时，可在 Player Settings 的 Scripting Define Symbols 添加 `SPACECANVAS_DEVELOPER_MENUS`。默认公开的工程导入菜单为 **导入PICO工程 → 导入工程文件**。

## 5. 导入 JSON 与二次开发

头显保存路径由 `Application.persistentDataPath/Projects/` 决定。通过 USB 调试从 Unity 导入头显工程，或选取本地 `.spacecanvas` 文件。模型 ID 与预制体映射见素材来源和格式文档。

仅复用开放格式时，可以复制独立编辑器脚本到空 Unity 工程，无需安装 PICO SDK。若只带 JSON，不带模型资源，则无法自动获得同样的高精度模型；自行提供对应 assetId 的 prefab 即可替换占位体。

## 6. 常见问题与操作步骤

- **UPM 提示本地包不存在**：关闭 Unity，按第 2 步恢复 SDK；检查目标目录下直接包含 package.json，不能多嵌套一层。
- **EntityId 或 objectReferenceInstanceIDValue 编译错误**：确认 Unity 6000.6.8f1，再重新运行恢复脚本。若仍报错，核对具体 SDK 文件版本。
- **OpenXR 找不到 loader**：检查 SDK 内 `Runtime/Android/LoaderForUnitySDK.aar` 存在，并运行恢复脚本。
- **无法从头显读取工程**：连接 USB，允许调试，确保 Android 包名一致且已在头显保存；失败时导入菜单支持本地文件。
- **覆盖安装后没有启动**：保持头显唤醒，先回系统主页退出其他独占应用，再启动 SpaceCanvas。
- **GitHub 推送认证失效**：在电脑的 Git Credential Manager 登录 GitHub；不要把令牌写入仓库或聊天。组织仓库需要该账号拥有写入权限。

## 7. 当前范围

当前实际构建是 Full Space MR / OpenXR。早期 Shared Space/窗口适配代码保留用于开发参考。136 项 EditMode 回归和现有真机反馈针对当前本地依赖；恢复到新的电脑后仍需重新执行测试与设备验证。
