# Unity 6 MR UI 清晰度修复

## 面板轮廓抗锯齿

用户在 TMP 修复后确认“文字改善了，但面板边缘仍闪”。原 `SpaceCanvasRoundedPanel` 使用圆角三角扇和 1px 细边，轮廓为硬几何边缘。现改为解析圆角矩形：在片元中计算到轮廓的距离，使用 `fwidth` 按左右眼实际屏幕像素宽度计算透明度覆盖率，细边框保留至少 1.25 屏幕像素的宽度，减少亚像素闪烁。

- 新着色器：`Assets/Scripts/ProjectIO/Presentation/Shaders/RoundedPanel.shader`。保留 uGUI Stencil、RectMask2D、CanvasGroup 透明度、深度测试与 XR 双眼实例化宏。
- 每个面板的尺寸、圆角和边框颜色分别写入顶点 UV0、UV1、UV2；材质共用，悬停反馈不会改写其他面板的参数。
- 网格绘制边缘外扩 3 UI 单位，留出透明度渐变区域；RectTransform、射线命中范围与空间位置保持原值。
- `SpaceCanvasPanelMaterialBuilder` 生成并检查 Resources 材质，构建时保留着色器引用，避免 Android 裁剪后面板消失。
- 新增实际像素渲染回归：校验面板中心不透明、圆角透明、轮廓存在部分覆盖像素，并检查共用材质的参数隔离、UV 通道与原命中尺寸。像素测试图片输出到 `Logs/Panel-AA-Validation.png`。

## 后续定位：旧 TMP 着色器与 Unity 6 网格格式不匹配

第一轮 MSAA 与字号调整后，用户确认闪烁仍存在。继续逐行检查发现：当前 `com.unity.ugui` 的 `TextMeshProUGUI` 将 signed SDF scale 写入 `characterInfo.vertex_*.uv.w`（UV0.w），而工程中保留的旧版 `TMP_SDF-Mobile.shader`、标准 SDF 等仍读取 `input.texcoord1.y`（UV1.y）。后者在当前网格中是布局 UV，不是字形缩放值。

这属于已确认的着色器数据接口错误，会使 SDF 边缘宽度和粗细计算失真；MSAA 无法纠正读取错误的数据。此前把抗锯齿作为主要原因的判断不完整。

修复从当前安装包 `com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage` 提取并更新 16 个工程已有的官方 shader/cginc，保持 `.meta`、字体、材质与图集不变。没有手工编造新的文字着色器，也没有引入插件。更新清单与前后 SHA256 位于 `Logs/TMP6-ShaderUpgrade.json`，原文件和 APK 备份位于 `Builds/Unity6-BeforeTMPShaderFix/`。

新增 `Editor/SpaceCanvasTmpShaderCompatibility.cs`，构建前比对实际安装的 TMP 网格写入格式与主要 SDF 着色器读取格式，不匹配时停止构建并给出中文提示。回归包含旧版着色器应被拒绝、两种匹配接口可被接受，以及当前实际安装着色器通过检查。完整 EditMode：134 通过、0 失败、0 跳过，报告 `Logs/Unity6-TMPShader-EditMode.xml`。

已读取上一版真机 60 秒帧间隔报告：p95 约 11.1112ms，p99 约 11.1287ms；报告没有有效 GPU samples，不能据此作 GPU 归因或完整性能验收。原始数据位于 `Logs/session-output-20261106-UIRecheck/performance-data/`。这项记录只描述已有样本；没有额外进行 Perfetto 性能根因分析。

文字数据接口修复已在源码和 EditMode 中验证；真机转头时是否消除可见闪烁，仍需在新包上目视确认。

针对转头时文字发虚、面板细边闪烁的问题，本次调整如下。

- Android 使用的 Performant URP 资源开启 4× MSAA，主 XR 相机允许 MSAA。保持 HDR 关闭和渲染比例 1，减少额外带宽开销并保留透视背景。
- 世界空间 Canvas 禁止像素吸附，补齐 TMP SDF 使用的 TexCoord1、Normal、Tangent 通道，包括独立排序的子 Canvas。
- 固定字号的辅助文字最低 18px，为中文行高补足空间；保留标题字号、自动字号规则、面板位置和射线命中关系。提高新建辅助文字的对比度。
- 按钮颜色统一由 SpaceCanvasButtonFeedback 控制，避免与 Selectable ColorTint 叠加。动画收敛后停止重复写入颜色，禁用按钮保留低透明度反馈。

入口为 `SpaceCanvasUIClarity.Configure`，启动时处理已有面板；新建 Dock 和 Label 通过样式工厂处理。此方法可重复调用，不累计放大字号，也不移动工作台。

## 验证方法

EditMode 回归包括文字配置幂等、中文行高、隐藏子 Canvas 的通道、面板排序/位置/命中属性不变，以及原有导入导出和交互测试。

真机应分别检查：缓慢左右转头时素材名称和边缘是否稳定；快速转头是否出现重影；透视背景与半透明放置预览是否正常；按钮点击与参数编辑是否正常。需保持头显唤醒后再评估帧率。启动日志和构建成功不能替代这些目视确认。

MSAA 主要改善几何边缘；文本仍依赖 SDF 字体、可读字号和足够的屏幕像素。若真机仍在快速转头时模糊，应继续根据实际 GPU 帧时间和显示刷新率排查，避免直接堆高渲染比例导致掉帧。

## 本次验证记录（2026-11-06）

- 完整 EditMode 测试：131 通过、0 失败、0 跳过。报告为 `Logs/Unity6-UIClarity-EditMode-Verified.xml`。
- Android 构建：成功，0 错误、69 警告，APK 签名校验通过。
- APK：`Builds/SpaceCanvas-FullSpaceMR.apk`，SHA256 `88F4DA9D89CBCEB4F5812136C18A595C91AAE4A236A167D93A6B22660BB8F545`。
- 已安装到 Swan `PB314XHGKC160016G`，进程 17311 启动并位于前台；日志确认 9 个物体和透视背景加载。日志为 `Logs/Unity6-UIClarity-Startup.json`。
- 真机闪烁改善程度尚待用户目视确认，未将启动成功计为画质或性能验收。启动时仍有原先的 Play AssetPack 类缺失日志，应用随后进入工作台，本次未处理该独立问题。
