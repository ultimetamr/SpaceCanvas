# PICO Unity SDK 6.1.1 能力清单与接入边界

> 菜单已精简：默认仅显示“导入PICO工程 > 导入工程文件”。本文提及的 Tools > SpaceCanvas 开发菜单仅在启用 SPACECANVAS_DEVELOPER_MENUS 编译符号后显示；底层构建、验收和修复方法仍保留。

本项目依据 `D:\DENO\PICO-Unity-SDK-6.1.1-20260903.zip` 做过离线清单扫描，并在 Unity 2022.3 Android 工程中复核。扫描只用于确认公开程序集和 Building Blocks 的实际名称，不复制厂商代码。

## 已使用的公开能力

- PICO OpenXR Loader：负责头显渲染、控制器输入和手势输入。
- Passthrough/OpenXR MR：在 Full Space 中显示真实环境透视。
- SpatialAdapter 的 `SpatialCamera` 与空间模式同步组件：用于检测和同步空间模式。
- XR Hands / Input System：统一手柄、手势和输入动作，射线丢失时自动释放 UI 按下状态。
- Unity `TouchScreenKeyboard`：工程名输入时尝试唤起系统软键盘，系统不提供键盘时保留 TMP 输入框。

## 当前 SDK 扫描到的空间组件

SDK 6.1.1 包中可确认的入口包括 `SpatialCamera`、`PXR_SpatialCameraSpatialModeSynchronizer`、`SpatialNativeApi`、Stage Space / Volume Space 摄像机 Building Block，以及空间锚点和空间网格能力。

本项目的能力矩阵还会在运行时扫描公开程序集，分别报告 SpatialCamera、Shared Space 模式、原生 Planar 创建 API、原生 Volumetric 创建 API、系统键盘和眼动设备。检测到类型不等于系统窗口已打开，因此报告不会把 Unity Canvas 伪称为系统原生窗口。

## 交付模式

当前可稳定运行的真机入口是 **PICO OpenXR + Full Space MR + Passthrough + Unity 世界空间 UI**。它提供完整的工程编辑、JSON 导入导出、射线交互、手势和手柄输入、示例工程与撤销链路。

SDK 当前没有向本项目公开可安全调用的多窗口创建方法时，`SpaceCanvasSwanWindowHost` 会保留 AssetLibrary、Properties、Toolbar、VolumetricScene 四个逻辑槽位，并通过平滑跟随、距离边界和自动回正保持稳定布局。未来 SDK 提供正式窗口 API 后，只需在该适配层替换创建实现，不需要改工程格式或导入导出逻辑。

## 复核入口

在 Unity 菜单执行：

- `Tools > SpaceCanvas > 5星验收 > 运行构建前检查`
- `Tools > SpaceCanvas > 5星验收 > 生成完整验收报告`

关于开源面板会展示同一能力矩阵。评审材料应以报告中的实际状态为准。
