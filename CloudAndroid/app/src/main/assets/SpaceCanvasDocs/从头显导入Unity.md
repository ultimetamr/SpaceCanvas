# 从 PICO 保存的工程导入 Unity

1. 在头显中的 SpaceCanvas 点击“保存工程”。
2. 将头显通过 USB 连接到电脑，允许 USB 调试。
3. 在 Unity 编辑模式点击 `导入PICO工程 > 导入工程文件`。
4. 工具自动读取头显 `/sdcard/Android/data/com.spacecanvas.prototype/files/Projects`，复制到本 Unity 工程的 `DeviceProjects/<设备序列号>/<本次批次>/`，随后打开版本选择窗口。
5. 左侧按工程 JSON 的保存时间倒序排列，首个有效版本标记“最新”。选择条目后，右侧显示完整文件路径、工程名、保存时间、物体/分组数量、格式版本、文件指纹及模型缩略图。核对后点击“确认导入此版本”，再保存 Scene。建议在单独的新 Scene 导入。

头显路径才是已保存工程的来源；`DeviceProjects` 是电脑缓存。每次读取使用新批次目录，不修改头显数据，不覆盖之前的电脑副本。离线时在读取失败提示中选择“选择本地文件”，随后可在版本窗口中选择电脑文件。工具记住本工程上次成功导入的位置。

版本列表优先使用 JSON 的 `updateTime`（UTC 秒），同秒保存使用可选 `savedAtUtcMilliseconds` 排序，不会被 USB 复制时间打乱。旧存档缺少有效保存时间时使用文件时间，并明确标注“文件时间”。时间显示按电脑/头显各自的本地时区转换，设备时钟错误时应核对文件名、预览和指纹。

文件指纹是实际读取字节的 SHA-256 前 8 位，用于快速区分版本，不作为安全认证。列表读取、确认预览与实际导入使用同一份数据快照；确认期间即使源文件发生变化，也不会换成别的内容。需要读取文件变化时点击“刷新此目录”。损坏文件显示错误并禁止确认导入。

新版头显保存会为当前可编辑模型生成一张 320×200 PNG 并内嵌在 JSON 中，只包含模型，不采集透视画面或 UI。旧文件不会自动补图；需要在更新后的应用里再次保存。没有缩略图、预览损坏或无法生成时显示“暂无预览”，不影响布局保存和导入。每张 PNG 最大 256 KB，解码尺寸上限 1024×1024；工程版本读取上限 16 MB。

需要 Unity 自带 Android Build Support 的 SDK/platform-tools/adb，或 PATH/ANDROID_SDK_ROOT/ANDROID_HOME 指向的已有 ADB。不需要 PICO SDK 或额外 Unity 插件。多台设备在线时请只保留目标 PICO；没有授权、未连接或尚未保存过工程时，会显示中文提示，可选择本地文件或取消。读取过程有超时保护。

使用其他 Android 包名时，请同步修改 `SpaceCanvasEditorImporter.cs` 内的 `RemoteProjectsDirectory`。独立复制该脚本到其他 Unity 工程的 `Assets/Editor/` 后仍可使用以上入口。

本工程的编辑器导入器通过 `SpaceCanvasEditorAssetBridge.cs` 复用头显运行时的素材库和外观实现：优先使用 SpaceCanvasCurated 中的真实模型，已知内置 ID 使用相同的精细模型工厂，再处理自定义 prefab。保留每个材质槽、贴图、分部位配色、透明度、金属度以及碰撞体开关。生成的材质保存到 `Assets/SpaceCanvasImportedMaterials/`，保存、重新打开 Scene 后仍保留。

JSON 保存布局和物体参数，不包含网格、贴图。第三方只复制独立脚本 `SpaceCanvasEditorImporter.cs` 到 `Assets/Editor/` 时，无需 SpaceCanvas 或 PICO SDK；提供对应的 `Resources/SpaceCanvasCurated/{assetId}.prefab` 或 `Resources/SpaceCanvasAssets/{assetId}.prefab` 及其依赖，才能还原相同精细模型。没有资源时生成占位体并报告缺失素材，不能从纯 JSON 凭空生成原网格和贴图。

导入生成的是普通 Unity Scene 布局，挂在工程名根节点下，不会自动注册到 MR 编辑工作台或替换启动场景的示例。头显工作台显示可能有整体缩放和摆放偏移；编辑器根节点保持单位缩放，按 JSON 工程局部坐标还原。需要查看全部物体时，在 Hierarchy 双击工程根节点聚焦。

旧版本已经导入的简模和纯色材质不会自动更新，请在新 Scene 重新导入对应文件。不要在旧导入结果上反复叠加，否则容易把不同保存版本混在一起。

2026-11-03 验证：新增 5 项 EditMode 回归全部通过，覆盖 36 个内置素材与运行时的网格、材质槽、贴图和染色一致性，父子局部变换，实际头显保存文件的变换，独立 prefab 路径，以及 Scene 保存重开后的材质。另在不含 PICO/SpaceCanvas 运行时程序集的 Unity 2022.3 空工程中，只使用独立导入脚本还原 3 个官方示例，层级、变换、PBR 和碰撞体检查均通过。

实际对照场景：`Assets/Scenes/SpaceCanvas_ImportedHeadset.unity`。来源是本次已读取的 `Living Room Demo_7.spacecanvas`，导入成功 9 个物体，缺失素材 0，导入警告 0。该场景用于查看保存布局；Android 构建入口仍是 Full Space MR 工作台场景。
