# SpaceCanvas Swan 接入与验证状态

> 菜单已精简：默认仅显示“导入PICO工程 > 导入工程文件”。本文提及的 Tools > SpaceCanvas 开发菜单仅在启用 SPACECANVAS_DEVELOPER_MENUS 编译符号后显示；底层构建、验收和修复方法仍保留。

更新日期：2026-10-29。工程 D:/Would/DOME/SpaceCanvas，Unity 2022.3.62f3c1。

## 当前真机入口：Full Space MR

打开 Assets/Scenes/SpaceCanvas_FullSpaceMR.unity；菜单 Tools > SpaceCanvas > 打开 Full Space MR 工作台。Android 构建菜单为 Tools > SpaceCanvas > 构建 Full Space MR Android APK，输出 Builds/SpaceCanvas-FullSpaceMR.apk，包名 com.spacecanvas.prototype。

当前采用 PICO OpenXR Loader + Unity 渲染 + PICO PassthroughFeature 透视，在独占 Full Space 中显示已保存的客厅工作台、素材库、属性面板与保存/导入/关于开源/撤销按钮。不是四个系统原生 Planar/Volumetric 窗口，不支持以此模式验收 Shared Space 多应用共存。此前的 SpaceCanvas_MR.unity 保留用于追溯；该 Shared Space 入口在当前真机发生协议不匹配，不作为可用设备入口。SpaceCanvas_Start.unity 用于桌面开发。

## 输入与工作台放置

XR/SpaceCanvasOpenXRPointer 通过显式启用的 OpenXR InputActionMap 请求左右手或手柄的 pose、tracking、trigger/pinch/select，并使用 XRI XRRayInteractor、TrackedDeviceGraphicRaycaster、XRUIInputModule 分发世界空间 UI 事件。射线材质由构建工具持久化到 Resources，避免空材质及 Android shader 裁剪。相同射线在非 UI 区域支持选择和编辑物体；丢失追踪取消编辑，重新追踪后需先释放再按下。

FullSpaceRuntime 启用头部追踪动作，首次获得有效头部姿态后在前方 1.5 米放置工作台；不硬绑定头部。追踪延迟、未佩戴或系统安全区域流程结束后仍会继续尝试首次放置。Android 使用移动质量等级并关闭主相机 HDR/MSAA。

依赖版本：PICO Unity SDK 6.1.1-20260903；Input System 1.14.0；XR Management 4.5.4；OpenXR 1.14.2；XRI 3.3.0；XR Core Utils 2.5.2；官方 XR Hands 1.4.3。SDK 设置为 XR 模式，关闭 SpatialAdapter，开启 ControllersAndHands、HandInteractionProfile、HandTracking 和透视。manifest 使用 UnityPlayerActivity，包含 HAND_TRACKING 权限。

## 已核验与待核验

- 真机安装并启动成功；用户已确认看到工作台和按钮。
- 更新输入绑定及 XR Hands 后，真机日志实际出现左右 HandInteractionOpenXR pointer acquired、tracked=True、passthrough=True、首次头部定位成功。
- 现场确认射线已可见、按钮仍无法点击。后续日志同时报告骨骼、手势和 PICO4Ultra 手柄 profile，发现射线误选没有按键的 XRHandDevice。已过滤只有骨骼姿态的设备，优先选择同侧已追踪的可点击控制器/手势 profile；去除上一帧 UI 命中对当前点击的过滤，更新包已现场复测：用户确认“关于开源”能打开，“导入工程 → 官方示例 → 办公工位 → 确认导入”能正常切换。开发构建新增 UI down/up/click 日志，不能用获取输入日志替代实际点击验收。
- 眼动交互未完成。原生 EyeTrackingClient 仍报告共享内存 fd 不可用；当前设置 isEyeTracking=0，不宣称已解决 ROM/SDK 内部眼动问题。
- 更新移动渲染设置后的应用进程日志暂未发现此前 GL_INVALID_OPERATION；此结论仅限本次采集范围。
- 现有 CLI/设备不提供空间 FOV 截图，因此没有用普通 2D 截图冒充空间证据。
- 23 项真实 EditMode 测试已通过（含输入设备选择、追踪丢失释放 UI 的两项回归）；覆盖全部字段、三个示例、层级、整次导入撤销、20 步撤销栈、参数等价比较、文件名规范化及非法数据。

2026-10-30 真机复核：PICO Swan B3110（Android 16，`PB314XHGKC160016G`）已成功安装并启动 `SpaceCanvas-FullSpaceMR.apk`。用户现场确认工作台、按钮、射线悬停反馈、“关于开源”和“官方示例 → 办公工位”导入可用；本轮日志未发现应用崩溃。设备进入休眠后 Unity Activity 收到 `onStop`，属于系统生命周期。当前 `pico-cli 0.5.0` 不支持空间 FOV 截图参数，普通截图在 XR 显示模式下无法生成有效 PNG，因此不将普通截图作为空间验收证据。

工程名 TMP 输入框已通过 `SpaceCanvasSystemKeyboardBridge` 尝试打开 Unity/Android 系统软键盘；PICO ROM 是否显示键盘由系统输入法策略决定，键盘不可用时仍可直接使用 TMP 输入框。新增 `SpaceCanvasEyeGazeInteractor` 只在公开 EyeTracking InputDevice 时启用视线悬停/可选驻留点击，未检测到眼动时自动保留手柄/手势射线。`PicoSpatialCapabilityReport` 会把 SpatialCamera、Shared Space 模式、多窗口 API、系统键盘和眼动状态同时显示在“关于开源”面板与日志中。轻量 3D AudioSource 已连接，头显实际声音仍需现场确认。

当前 SDK 未暴露可安全调用的多个系统级 Planar/Volumetric 创建 API，交付入口仍是 Full Space MR fallback；能力矩阵和 5 星验收步骤见 `5星验收与能力矩阵.md`，评审材料应按真实报告填写。

## JSON 数据链路与二次开发

保存使用 Application.persistentDataPath/Projects，UTF-8 JSON、重名追加编号。示例通过 UnityWebRequest 读取 StreamingAssets，兼容 Android APK jar 路径。导入先验证、暂存再替换可编辑内容；保留相机和全局管理器。一次导入形成一个撤销快照，最多 20 步。保存会更新 updateTime，单位偏好不改变坐标/gridSize 的米制含义。

Tools > SpaceCanvas > 测试 > 运行完整 EditMode 验证 运行实际 Unity 测试。报告 Library/SpaceCanvas.Validation.txt、Library/SpaceCanvas.EditMode.Results.xml。Full Space 构建报告 Library/SpaceCanvas.FullSpaceBuild.txt 和 .FullSpaceBuild.Diagnostics.txt。

完整运行时还需 Newtonsoft 3.0.2、uGUI、TMP。普通 Unity 移植可省略 Spatial/、XR/、Editor/Spatial/、Editor/XR/ 以及两个 MR 场景和对应配置。独立还原只需复制 Editor/SpaceCanvasEditorImporter.cs 到空 Unity 的 Assets/Editor，再导入 .spacecanvas；不依赖 PICO、Newtonsoft 或业务脚本。

## 历史 Shared Space 修复说明

嵌入厂商包此前修复：Spatial 模式不加载经典 PxrPlatform；Android Activity 调用排除 Windows Editor；SpatialAdapter Resources 路径统一项目相对路径；Collections 2.1.4 匹配 DLL；使用 SDK 对应 PICO glTF/USD 分支。升级时需复核这些改动。厂商 SDK 不在自有源码包中分发。

当前 Full Space 构建不再使用这些 SpatialAdapter 渲染路径。未修改参考工程 D:/Would/DOME/my-pico-unity-app。发布前需填写真实 PICO appID，核验构建警告和设备生命周期；不可编造 appID。

## 素材质量升级

运行时内置素材保留原有 36 个 `assetId`，但优先使用 `SpaceCanvasPremiumAssets` 程序化生成层，补充了沙发坐垫、桌面层、椅背、办公椅底座、显示器支架、键盘按键、植物叶片、柜门和展会结构等细节。若项目需要写实模型，可把同名 prefab 放入 `Assets/Resources/SpaceCanvasAssets/`，并从 `SpaceCanvasBuiltinAssets` 中移除对应 ID 后接入 FBX/GLB 导入资产。

旧场景升级：执行 `Tools > SpaceCanvas > 素材 > 升级当前场景为高质量模型`。该迁移只替换模型根节点，保留工程对象 ID、父子层级、局部坐标、旋转、缩放、颜色、透明度、金属度和碰撞体开关。


