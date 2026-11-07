# 作品提交资料

- **作品名称：** SpaceCanvas 空间原型坊
- **版本名称：** 0.3.2（Unity 6 MR 体验与清晰度修复版）
- **Android versionName：** 0.3.2
- **Android versionCode：** 5
- **应用包名：** com.spacecanvas.prototype
- **项目 GitHub：** https://github.com/ultimetamr/SpaceCanvas
- **作品图标：** `Assets/Scripts/ProjectIO/Editor/Branding/SpaceCanvasIcon.png`，1024 × 1024 PNG
- **一句话简介：** 在真实空间中搭建布局，将灵感保存为可编辑、可共享、可导入 Unity 的开放工程。

## 作品简介

SpaceCanvas 空间原型坊是一款面向 PICO Swan 的 MR 空间原型与布局工具。用户可在真实环境的透视背景中，从家居、办公、展会和装饰素材库选择物体，通过射线与半透明预览完成放置，再使用空间手柄、数值面板和外观工具精细调整布局。工程支持保存、预览、撤销与再次编辑，并内置客厅、展位和工位示例及新手教学。

作品采用开放的 `.spacecanvas` JSON 格式记录素材、层级、坐标、尺寸、颜色与分组，提供 Unity 编辑器导入工具和格式文档，让开发者能够将头显中创作的空间布局带回 Unity，替换模型、添加交互并继续开发。当前版本基于 Unity 6 与 Full Space MR / OpenXR，使用灰金视觉界面并优化了文字和面板边缘的清晰度。

## 版本说明（提交平台可直接使用）

完善 MR 素材放置、移动旋转缩放、外观配色和撤销；支持开放 JSON 工程保存与 Unity 场景还原，提供存档缩略图、版本预览、三套示例及新手教学。完成 Unity 6 适配，修复半透明放置流程、TMP 文字着色器兼容与面板边缘闪烁。公开自有源码、场景资源和二次开发文档。

## 开源范围

自有代码和格式文档采用 MIT；第三方组件保留原有许可。PICO SDK 的本地许可证禁止未经书面许可再分发，因此公开库不附带 SDK 本体，接入者按 `docs/SETUP.md` 自行恢复合法获取的 SDK。
