
# XREAL Android Stable Screen

独立 Android 稳屏客户端 Unity 工程，当前成品版本 **0.8.1**（versionCode 17，包名 `dev.xreal.stablescreen`）。通过用户授权的 MediaProjection 捕获手机屏幕，在眼镜中显示 3DoF / 6DoF 固定屏幕，并提供手势触控、左手掌承载和真实平面锚定功能。

Server 独立位于 [xreal-android-server](https://github.com/MainLe/xreal-android-server)。本仓库不是 Server，运行时需先安装并启动相容的 Server 1.0.1。最低 Android API 32。

## 构建依赖

- Unity **2022.3.62f3c1**，Android Build Support、IL2CPP、ARM64。
- XREAL XR Plugin **3.1.0**，自行取得后解包到根目录 `package/`。`Packages/manifest.json` 保留 `file:../package` 本地引用。
- JDK 17、Android SDK platform 36 / build-tools 35.0.0、NDK 25.1.8937393；Python 3 和 `requirements.txt` 中的工具依赖。
- 自行取得与 Server 构建相同的 `ControlGlasses-3.1.0.20251118115716-release.apk`，放在仓库根目录，供精确版本匹配的手部兼容库构建使用。

厂商 SDK、APK、第三方工具安装目录、Unity 缓存、签名密钥和个人手机截图不进入源码分支。TMP Essential Resources 由 Unity 自带的 TextMeshPro 3.0.7 导入；其标准资源 GUID 与项目引用一致。

## 准备与构建

```powershell
python prepare_workspace.py
python -m pip install -r requirements.txt
$env:XREAL_ANDROID_SDK = 'C:\Android\Sdk'
$env:XREAL_JDK = 'C:\Program Files\Java\jdk-17'
```

1. 用 Unity 打开工程，等待 `Packages` 解析完成，切换到 Android。使用 **Window > TextMeshPro > Import TMP Essential Resources**，或在 Unity 完成包解析后运行 `python ServerAppResearch/tools/import_tmp_resources.py`。
2. 本工程保留了一份仅为运行审计候选构建增加条件诊断的 SDK 托管 provider 快照：`sdk-overrides/XREALHandSubsystem.cs`。如需编译项目中的诊断构建，把它复制到 `package/Runtime/Scripts/Android/XREALHandSubsystem.cs`；Stable Screen 不启用 `XREAL_HAND_RUNTIME_AUDIT`，不修改厂商原生跟踪库。
3. 运行 `python ServerAppResearch/build_hand_compat.py` 生成进程内手部兼容库，再运行 `python ServerAppResearch/prepare_screen_stabilizer.py` 准备本应用使用的显示适配 AAR，并运行 Java 编译/帧邮箱检查。后者还需要 Unity Android Player 的 `classes.jar`，脚本默认查找原工作区使用的 Unity 安装路径；其他安装路径需在脚本中调整该 fallback。
4. Unity 菜单选择 **XREAL > Build Stable Phone Screen APK**。输出 `Build/Android/XREAL-StableScreen-v0.8.1.apk`。根目录 `Build-Android.ps1` 是较早的 HelloMR 示例构建入口，不是稳屏构建入口。

## 检查与范围

`Assets/Editor` 包含姿态、手势、触控、平面和锚点生命周期检查。`ServerAppResearch/test_phone_gesture_service.py` 是离线 Java 状态机检查；`prepare_screen_stabilizer.py` 包含 10000 帧邮箱并发检查。某些研究/核验脚本保留原本机工具路径，并要求旧版 APK 或原 SDK 输入，见脚本开头。

0.8.1 已完成离线构建、签名、ZIP 对齐、Shader 与打包核验；平面锚点创建修复仍需新的实机确认。详见 [0.8.1 修复说明](ServerAppResearch/screen-stabilizer/ANCHOR-CREATION-081.md) 和 [使用说明](docs)。核验脚本引用的历史 APK、原始设备日志和截图未随源码公开。

`PhoneScreenshot.png` 是可选静态对照场景的本机素材，已排除；如需构建该对照场景，应自行提供素材。现有稳屏 APK 的校验值见 [artifact.json](release/artifact.json)，APK 作为 Release 附件管理，不写入源码分支。
