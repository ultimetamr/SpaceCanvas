# 当前版本验证摘要

记录日期：2026-11-07。对应 versionName 0.3.2 / versionCode 5。

- Unity：6000.6.8f1；当前启动场景：`Assets/Scenes/SpaceCanvas_FullSpaceMR.unity`。
- 当前运行方式：Full Space MR / OpenXR，Android，PICO Swan。
- 最近一轮本地 EditMode：136 通过，0 失败，0 跳过。原始报告在本机 `Logs/Unity6-PanelAA-EditMode-Final.xml`，运行日志不随公开源代码分发。
- Android 构建成功，记录为 0 错误、69 警告；APK 签名校验通过。
- 对应 APK SHA256：`09B16C80EF0B00534AF8B392269ECD48D3D8B30B8DBFF9C3E7BA590106B6F791`。APK 本体不在源码 Git 历史中。
- 用户真机反馈：文字改善；面板边缘明显改善，点击正常。此前已验证半透明预览与放置位置、工程列表和示例切换等流程。

本摘要整理现有本机记录，发布 README 本身没有再次构建 APK 或重跑 Unity 测试。更换 SDK、Unity 版本或设备后，应重新编译并测试。公开库排除了受限 SDK；恢复脚本可以复用当前兼容修复，但全新下载的 SDK 若结构不同会明确报错，不能视为任意 SDK 版本均已验证。
