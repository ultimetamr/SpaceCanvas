# 当前版本验证摘要

## 0.3.3 工作空间旋转（2026-11-20）

- 从公开仓库恢复至 `D:/Would/DOME/SpaceCanvas`，恢复本机合法持有的 PICO SDK 6.1.1 / SpatialAdapter 6.1.0；未将 SDK 开发源码加入仓库。
- Unity 6000.6.8f1，Android / Full Space MR / OpenXR。
- EditMode：144 项，142 通过，0 失败，2 跳过。跳过项需要已下载的头显存档，本次没有该文件；其余固定数据测试已执行。报告 `Logs/WorkspaceView.FinalTests.xml`。
- 新增 8 项测试覆盖查看层的数据不变性、复位、导入撤销、俯视拖动与放置、容器适配、按钮绑定与真实启动场景的 XR/弹窗行为。
- 渲染预览已检查常规工作台和俯视；俯视将内容容器临时缩至 72% 并上移 0.12 米以避开下方工具条，复位后恢复精确原始变换。预览不代表真机视觉验收。
- Unity Android APK 构建成功：0 错误、41 警告，主要是现有 API 弃用和 SDK 提示。versionName `0.3.3` / versionCode `6` / 包名 `com.spacecanvas.prototype` / API 36。
- Unity APK SHA256：`ACD8DFB37FDFC59113F6A29AA3A84D2DF578856032E6C7C1BF23E8D8817E4C06`。
- 新云快照含 548 个文件，`sourceCommit=7c1c7f0cae694123ca601b89b033cd7aad7c55ad`。
- Gradle 8.11.1 / Java 17 根目录 `assembleRelease` 成功；快照摘要检查通过。最终 APK 的 513 个 assets/native 文件与快照逐项相同，签名验证通过。
- 云打包验证 APK SHA256：`91BA853124AE20D836E14F52FD0D6703E4EAE5F69EE29705576F412D2183D7B6`。
- 本次未检测到 ADB 设备，尚未安装到 Swan；左右转动手感、手势和手柄点击、MR 中的遮挡与舒适性、保存后头显文件回传 Unity 的人工检查仍待完成。GitHub 平台云任务需重新触发，本机 Gradle 成功不等于平台已执行。

## 0.3.2 历史验证

记录日期：2026-11-07。对应 versionName 0.3.2 / versionCode 5。

- Unity：6000.6.8f1；当前启动场景：`Assets/Scenes/SpaceCanvas_FullSpaceMR.unity`。
- 当前运行方式：Full Space MR / OpenXR，Android，PICO Swan。
- 最近一轮本地 EditMode：136 通过，0 失败，0 跳过。原始报告在本机 `Logs/Unity6-PanelAA-EditMode-Final.xml`，运行日志不随公开源代码分发。
- Android 构建成功，记录为 0 错误、69 警告；APK 签名校验通过。
- 对应 APK SHA256：`09B16C80EF0B00534AF8B392269ECD48D3D8B30B8DBFF9C3E7BA590106B6F791`。APK 本体不在源码 Git 历史中。
- 用户真机反馈：文字改善；面板边缘明显改善，点击正常。此前已验证半透明预览与放置位置、工程列表和示例切换等流程。

本摘要整理现有本机记录，发布 README 本身没有再次构建 APK 或重跑 Unity 测试。更换 SDK、Unity 版本或设备后，应重新编译并测试。公开库排除了受限 SDK；恢复脚本可以复用当前兼容修复，但全新下载的 SDK 若结构不同会明确报错，不能视为任意 SDK 版本均已验证。
