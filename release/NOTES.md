# XREAL Android Stable Screen 0.8.1

首次 GitHub Release，版本 `0.8.1` / versionCode 17 / `dev.xreal.stablescreen`，最低 Android API 32。

- Unity IL2CPP Release / ARM64；非 Development 构建，关闭脚本调试与自动连接 Profiler。
- 包含手机屏幕捕获、3DoF / 6DoF 固定屏幕、手势触控、左手掌承载和平面锚点功能。
- 保留 0.8.1 的锚点创建生命周期修正和有界重试。实际设备上的锚点创建修复仍需实机确认。
- 沿用现有本地开发签名证书，与原客户端安装签名一致。不是应用商店专用签名。

运行前安装并启动相容的 Android Server 1.0.1；屏幕捕获和手势服务由用户在手机上授权。
此次验证构建、签名、对齐、Shader 及打包内容，没有进行新的手机实测。

下载 `XREAL-StableScreen-v0.8.1-release.apk`；用 `SHA256SUMS` 校验下载内容。
