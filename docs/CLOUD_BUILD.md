# GitHub 云编译 APK

## 本次错误的原因

云平台在仓库根目录运行 `gradle assembleRelease`。原仓库是 Unity 源工程，根目录没有 `settings.gradle`，因此错误发生在 Gradle 发现工程阶段，尚未编译 C# 或 Android 源码。不要执行 `gradle init` 生成一个无关空应用。

Unity 本机生成的 Android 工程还使用 AGP 9.1.1 / Gradle 9.3.1 和 Windows 绝对路径，不能直接复制后交给日志中的 Gradle 8.11.1。

## 目前提供的构建方式

仓库根目录现在是真正的 Android Gradle 工程：

- `settings.gradle`：注册 `:app`，实际目录是 `CloudAndroid/app`。
- `build.gradle`：AGP 8.10.1，提供根目录 `assembleRelease` 和应用快照校验。
- `CloudAndroid/app/src/main`：Unity 生成的启动 Activity、Manifest、图标、资源、ARM64 原生运行库和场景数据。
- `CloudAndroid/app/libs`：应用运行需要的编译后 JAR/AAR，不包含 PICO SDK 开发源码包。
- `CloudAndroid/snapshot.json`：每个应用快照文件的 SHA256 和来源提交、版本信息，打包前逐项校验。

云平台编译 Java/Android 启动层并合并资源、运行库和 Unity 场景数据，生成包含实际 SpaceCanvas 内容的 APK。**不是下载现成 APK 冒充编译，也不是全量 Unity 源码重编译**。本次应用快照对应既有的 Unity 0.3.2 / code 5 构建，保留其编译时的 Unity Player 配置；Gradle 的 release 变体不会把 Development Player 自动变成 Unity 非开发 Player。

## 在提交平台怎么操作

1. 项目 GitHub URL 使用 `https://github.com/ultimetamr/SpaceCanvas`，选择 `main` 分支。
2. 将版本更新到包含本次云编译修复的提交；若页面缓存旧提交，刷新或重新选择仓库和分支。
3. 构建目录选择仓库根目录。若页面只有默认构建，不需要增加子目录，根目录入口已接入 `:app`。
4. 如可设置环境，使用 Gradle **8.11.1**、Java **17**、Android SDK Platform **36**、Build Tools **36.0.0**。本路径不调用 Unity/IL2CPP 编译器，不需要 NDK。
5. 点击重新编译。构建日志应先加载 `SpaceCanvas` 和 `:app`，随后出现 `Verified Unity ... snapshot`，最终生成 `CloudAndroid/app/build/outputs/apk/release/app-release.apk`。
6. 如果平台仍出现“does not contain a Gradle build”，说明拉取的仍是旧提交或构建目录不对，核对本次提交号与根目录 `settings.gradle`。

平台只接受 GitHub 构建而不提供环境配置时，先重试新提交。如果下一步提示缺少 SDK、Java 或下载失败，需要平台提供对应构建环境；源代码不能替代云主机上的工具安装。继续提供新的完整首个报错即可定位。

## 本机模拟云端验证（2026-11-07）

- Gradle 8.11.1 / AGP 8.10.1 / Java 17 / Android SDK 36：根目录 `assembleRelease` 成功。
- 使用 init-script 模拟平台注入 versionCode=1：同一根目录任务再次成功；aapt 确认包名 `com.spacecanvas.prototype`、versionName `0.3.2`、versionCode `1`。
- 两份 APK 均通过签名校验；最终 APK 保留 19 个 ARM64 原生库。
- 核对 512 个场景资源和原生库文件与快照内容一致，0 差异。除移除开发电脑连接 IP 的 boot.config 外，快照场景资源与既有真机验证 APK 一致。
- 快照生成脚本在独立临时目录再次生成 547 个载荷文件，全部摘要一致。
- 默认 code 5 APK SHA256：`3D1CE0F137F579EDC227B640EEC1C60BD7C1140C22CA5FC9311DE477CCA03475`。
- 模拟注入 code 1 APK SHA256：`4E68A918C85185A4C167D06F0767C4E3C75C9357E618669E393E40A42CFACF0B`。

以上为 Windows 本机使用相同 Gradle 版本的构建验证，不代表已在用户的 Linux 云主机执行成功；云平台任务需用户重新触发。该次 Gradle APK 尚未另行上真机验证。本机 SDK 还装有 API 37.0，旧版 lint 无法解析其版本字符串，因此验证使用仅暴露 API 36 的隔离 SDK 目录，未修改或移除本机其他 SDK，也未关闭 release lint。

## 版本号与签名

应用默认 `versionName=0.3.2`、`versionCode=5`。用户提供的云端日志明确显示平台会注入 **versionCode=1**；这会改变最终 APK 的安装版本号。最终应以平台产物的 Manifest 为准，而不是只看工程设置。

本地测试使用 Android 自动生成的调试签名，签名文件和密码没有提交。云平台可能使用独立签名；若证书不同，无法直接覆盖已有真机安装。不要为解决签名或版本降级而直接卸载带存档的应用，先备份存档。正式发布需配置稳定的私有签名身份，不能把密钥放进公开 GitHub。

## 修改 C# / 模型 / 场景后必须更新快照

1. 关闭使用同一路径的其他 Unity 实例，恢复 SDK，使用 Unity 6000.6.8f1 构建当前已有 MR 场景。入口为 `SpaceCanvas.ProjectIO.Editor.XR.SpaceCanvasFullSpaceBuilder.BuildExistingAndroid`。
2. 确认 Unity 构建成功后，把当前 `CloudAndroid` 移到仓库外保留备份。不要只移动 C#，也不要丢弃既有备份。
3. 运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Update-CloudAndroidSnapshot.ps1`，它从当前本地 Unity Android 构建输出生成新的应用快照。
4. 从 Git 恢复 `CloudAndroid/app/build.gradle`（生成脚本只处理应用内容，不生成或覆盖此构建配置）；版本号与包名由快照自动读取，核对其与新 Unity 构建一致。
5. 执行 Gradle 构建，验证 APK 后提交新的 `CloudAndroid` 和对应 Unity 源码。`snapshot.json` 会重新记录文件摘要；不要手工改摘要掩盖漏更新。

脚本拒绝覆盖已有应用快照，不删除任何现有目录。场景数据中移除了原开发电脑的连接 IP，原生库和游戏资源保留 Unity 输出字节。不同 Unity/SDK 版本的输出布局可能不同，脚本会明确报出缺少文件。

官方背景参考：Unity Android Gradle project / Android Gradle Plugin 8.10 release notes。

- https://docs.unity3d.com/6000.0/Documentation/Manual/android-gradle-overview.html
- https://developer.android.com/build/releases/past-releases/agp-8-10-0-release-notes
