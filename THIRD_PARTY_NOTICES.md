# 第三方组件与授权边界

根目录 MIT License 仅覆盖本项目自有源码、格式文档、示例和原创原型资源。以下组件不被重新授权。

`CloudAndroid/app/libs/` 和 `CloudAndroid/app/src/main/jniLibs/` 是当前 SpaceCanvas Android 应用的编译后运行依赖，用于打包该应用；不是 MIT 开源 SDK。该目录不提供 PICO SDK 的编辑器、C# 开发包或源代码。Unity Player Activity、引擎运行库、OpenXR/PICO 原生运行库与第三方 JAR/AAR 的原始权利归各自权利人，不能将其解释为可独立提取后按 MIT 重新分发的 SDK。完整 Unity 源工程开发仍需自行获取相应 SDK。

- **PICO Unity SDK 6.1.1 / PICO SpatialAdapter 6.1.0**：本机包内 `LICENSE.md` 声明未经事先书面许可不得传播或复制。本仓库不发布这两个目录；请自行从 PICO 官方/已授权渠道获取，遵守其协议。恢复路径与本项目兼容调整见 `docs/SETUP.md`。
- **Unity registry 包**：UPM 根据 `Packages/manifest.json` 与 `packages-lock.json` 恢复。Unity Engine、URP、UGUI/TMP、Input System、OpenXR、XR Interaction Toolkit 等按各自协议使用。
- **TextMesh Pro Essential Resources**：`Assets/TextMesh Pro/` 来自 Unity 官方 UGUI/TMP 必需资源，保持官方着色器、字体和资源归属；相关许可说明见 `docs/licenses/Unity-UGUI-LICENSE.md`。
- **Unity USD**：`Packages/com.unity.formats.usd/` 原包保留 `LICENSE.md`、`NOTICE`、`Third Party Notices.md`，按 Apache-2.0 与其中列出的第三方条款使用。
- **Plattar glTF exporter / UnityGLTF**：`Packages/com.plattar.unitygltf/`，原包来源 `https://github.com/Plattar/gltf-exporter`，Apache-2.0；已补存上游 LICENSE，内部依赖保留其原有版权头。未将其宣称为 SpaceCanvas 原创代码。
- **Newtonsoft.Json**：Unity 官方 UPM 包 `com.unity.nuget.newtonsoft-json`，不额外加入其他 JSON 库。
- **Noto Sans SC**：`Assets/Resources/SpaceCanvasFonts/`，SIL Open Font License，原许可保留在该目录 `LICENSE.txt`。
- **Liberation Sans / EmojiOne**：保留 TMP 资源目录内的原授权或归属文件；不适用自有 MIT 授权。
- **Poly Haven 模型与贴图**：CC0 1.0。具体素材、作者、来源链接及许可正文位于 `Assets/StreamingAssets/SpaceCanvasDocs/真实家具素材来源与接入说明.md` 和 `PolyHaven-CC0-1.0.txt`。

本仓库未包含私人头显存档、签名密钥、GitHub 令牌、构建缓存或 APK。若为其他用途添加新素材或 SDK，请同时保留该组件自己的授权与归属记录。
