# SpaceCanvas 工程格式规范 v1.0

## 文件概述

`.spacecanvas` 本质是 UTF-8 编码的 JSON 纯文本文件。文件可以用文本编辑器、脚本或任意 JSON 工具直接查看和修改，允许自由二次开发与集成。Unity 运行时默认把本地工程保存到 `Application.persistentDataPath/Projects/`，文件名扩展名为 `.spacecanvas`。

## 根对象

| 字段 | 类型 | 含义 |
|---|---|---|
| `formatVersion` | string | 格式版本，当前为 `1.0`。主版本升高表示可能存在不兼容变化。 |
| `projectName` | string | 工程显示名称。 |
| `createTime` | long | Unix UTC 秒时间戳。 |
| `updateTime` | long | 最近更新时间的 Unix UTC 秒时间戳。 |
| `savedAtUtcMilliseconds` | long，可选 | 保存时的 Unix UTC 毫秒。仅当除以 1000 与 `updateTime` 一致时用于同秒存档排序；旧文件可缺省。 |
| `preview` | SpaceCanvasProjectPreview，可选 | 内嵌的模型预览；不含网格、贴图资源、透视或 UI，可缺省。 |
| `settings` | SceneSettings | 场景显示和单位设置。 |
| `objects` | SceneObject[] | 可编辑物体列表。 |
| `groups` | SceneGroup[] | 分组列表。 |

## SpaceCanvasProjectPreview（兼容 v1.0 的可选扩展）

| 字段 | 类型 | 含义与限制 |
|---|---|---|
| `encoding` | string | 当前只支持 `png-base64`。其他编码忽略预览，不影响工程导入。 |
| `pngBase64` | string | PNG 文件的 Base64 字符串；PNG 最大 256 KB，解码前尺寸上限 1024×1024；默认生成 320×200。 |
| `captureTime` | long | 模型预览生成时间，Unix UTC 秒。 |

缩略图只用于识别版本，布局以 `objects` 数据为准。旧工程不会自动补图，使用新版应用重新保存可生成。损坏或缺失的缩略图显示“暂无预览”。文件指纹由读取端计算实际字节的 SHA-256 前 8 位，用于快速区分版本，不写入 JSON，也不作为安全认证。工程版本浏览限制单文件不超过 16 MB。

## SceneSettings

`unitSystem` 为 `Meter`、`Centimeter` 或 `Millimeter`，表示界面显示单位偏好；`gridEnabled` 控制网格显示；`gridSize` 为大于 0 的浮点数，以米存储，默认 `0.25`。所有存储坐标统一为 Unity 米制，显示单位不改变文件数值。

## SceneObject

| 字段 | 类型 | 含义与范围 |
|---|---|---|
| `id` | string | 工程内唯一 ID。 |
| `assetId` | string | 素材库 ID。找不到时导入器使用默认立方体并输出警告。 |
| `parentId` | string | 父物体 ID；空字符串表示根节点。 |
| `localPosition` | float[3] | 局部位置 `[x,y,z]`，以米存储，使用 Unity 左手坐标系（Y 向上）。 |
| `localRotation` | float[4] | 四元数 `[x,y,z,w]`。 |
| `localScale` | float[3] | 局部缩放，通常为正数。 |
| `colorHex` | string | `#RRGGBB` 或 `#RRGGBBAA` 颜色。 |
| `alpha` | float | 透明度，范围 `0..1`。 |
| `metallic` | float | PBR 金属度，范围 `0..1`。 |
| `colliderEnabled` | bool | 是否启用物体及子节点物理碰撞体；关闭后仍可通过编辑射线选取、拖动和调整属性。 |

## SceneGroup

`groupId` 为唯一 ID，`groupName` 为显示名，`childIds` 是属于该分组的物体 ID 列表。分组关系与父子 Transform 关系相互独立。

## 内置素材 ID 对照表

| ID | 名称 | 分类 |
|---|---|---|
| `primitive.cube` | 立方体 | 基础体 |
| `primitive.sphere` | 球体 | 基础体 |
| `primitive.cylinder` | 圆柱体 | 基础体 |
| `primitive.capsule` | 胶囊体 | 基础体 |
| `primitive.plane` | 平面 | 基础体 |
| `furniture.sofa` | 沙发 | 客厅 |
| `furniture.armchair` | 单人椅 | 客厅 |
| `furniture.table` | 茶几 | 客厅 |
| `furniture.desk` | 书桌 | 家具 |
| `furniture.chair` | 餐椅 | 家具 |
| `furniture.shelf` | 置物架 | 家具 |
| `furniture.tv` | 电视 | 电器 |
| `furniture.lamp` | 落地灯 | 灯具 |
| `furniture.bed` | 床 | 卧室 |
| `kitchen.counter` | 操作台 | 厨房 |
| `kitchen.fridge` | 冰箱 | 厨房 |
| `kitchen.microwave` | 微波炉 | 厨房 |
| `kitchen.sink` | 水槽 | 厨房 |
| `office.monitor` | 显示器 | 办公 |
| `office.keyboard` | 键盘 | 办公 |
| `office.mouse` | 鼠标 | 办公 |
| `office.office_chair` | 办公椅 | 办公 |
| `office.partition` | 隔断 | 办公 |
| `office.plant` | 办公绿植 | 办公 |
| `exhibit.counter` | 展台 | 展会 |
| `exhibit.backwall` | 背墙 | 展会 |
| `exhibit.banner` | 易拉宝 | 展会 |
| `exhibit.spotlight` | 射灯 | 展会 |
| `exhibit.stool` | 展凳 | 展会 |
| `decor.plant` | 盆栽 | 装饰 |
| `decor.picture` | 装饰画 | 装饰 |
| `decor.rug` | 地毯 | 装饰 |
| `decor.clock` | 时钟 | 装饰 |
| `decor.book` | 书本 | 装饰 |
| `decor.vase` | 花瓶 | 装饰 |
| `decor.cabinet` | 收纳柜 | 装饰 |

## 二次开发指南

1. 将本目录示例 JSON 复制到 `StreamingAssets/ExampleProjects/`，或通过运行时保存工程生成文件。
2. 仅还原场景时，在空 Unity 工程创建 `Assets/Editor/`，只复制 `SpaceCanvasEditorImporter.cs` 即可；它使用 Unity 内置 JsonUtility，不依赖 SpaceCanvas、PICO 或 Newtonsoft。需要完整运行时编辑、保存及撤销时，再复制整个 `Assets/Scripts/ProjectIO/` 并安装 Unity Package Manager 的 `com.unity.nuget.newtonsoft-json` 包。
3. 选择 `导入PICO工程 > 导入工程文件`，即可在当前 Scene 生成根节点和所有物体。
4. 将高精度 prefab 放入 `Resources/SpaceCanvasAssets/{assetId}.prefab`，导入器会优先实例化该 prefab；也可以在素材库适配层中替换查找逻辑。
5. 扩展字段时，在数据类添加字段并为缺失字段提供默认值；旧导入器会忽略未知字段。建议保持 `formatVersion` 的主版本兼容，新增字段只做可选字段。
6. 自定义交互脚本可挂在 prefab 上，或在导入完成事件后按 `SceneObject.id` 查找对象。

## 版本兼容规则

缺失字段自动使用默认值，未知字段被忽略；低版本工程尽力导入。主版本高于当前版本时导入仍会继续，但控制台会提示。JSON 损坏会返回明确错误，不会让应用崩溃。

## 开源声明与协议

SpaceCanvas 自有 ProjectIO 代码、工程格式文档、示例文件和基础体原型素材按本目录 LICENSE.txt 中的 MIT License 发布，允许自由二次开发、修改、复制和集成，分发时保留版权及许可声明。PICO SDK、Unity 包、Noto 字体及其他第三方内容遵循各自许可，不包含在此授权中。
