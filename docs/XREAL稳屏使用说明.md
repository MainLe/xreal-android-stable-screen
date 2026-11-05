# XREAL 稳屏 0.3.2

0.3.2 只修复手机画面黑屏回归：投屏纹理和标定网格改用独立着色器、独立材质，修复 GLES 编译错误，切回投屏时显式恢复手机纹理绑定。构建和核验新增着色器失败拦截。本版本不调整防抖或跟踪策略。详细证据见 `ServerAppResearch/screen-stabilizer/BLACKSCREEN-032.md`。

0.3.1 修复“关闭滤波时，渲染前仍可能复用旧姿态并移动世界屏幕”的缺陷。升级后默认 **原生固定锚点**，与空间 Demo 共用严格的姿态读取；轻微 / 均衡 / 较强过滤保留为实验选项。新增 **静态标定网格**，暂停手机采集后用本地网格比较定位稳定性，以及有容量上限的长时间性能记录。此次未连接设备，视觉改善和长期故障是否消失仍待复测。调查详情见 `ServerAppResearch/screen-stabilizer/JITTER-AND-LONGRUN.md`。

把手机桌面和普通 APP 的原始画面投射到眼镜中的一个 3DoF / 6DoF 固定屏幕。手机继续正常触控。新版需要 Android 12L（API 32）及以上；小米 15 / Android 17 符合要求。

APK：`D:\Xreal\Build\Android\XREAL-StableScreen-v0.3.2.apk`。独立包名 `dev.xreal.stablescreen`，可覆盖旧版，不覆盖空间调试 Demo。配套使用已安装的 `XrealServer-v1.0.1.apk`，Server 无需更新。继承 0.3.0 的真实位置跟踪、30/60 fps 档位、动态测试页及旋转采集修复。

此前 0.2.0 已在小米 15 / Android 17 / Air 2 Ultra 上验证：Unity 启动、整屏授权、桌面及其他 APP 显示、转头时空间固定、横竖屏尺寸切换、停止后重新授权启动。眼镜渲染约 59–60 fps；这是渲染帧率，并非此前手机画面的采集帧率。上述旧版结果不代表 0.3.0 新功能已完成设备验收。

0.1.x 的 SDK Multi Resume 会在眼镜上启动 Activity，但当前系统强制镜像模式并返回 canHostTasks=false，拒绝该启动。0.2.0 改为在手机上保留 Unity 引擎 Activity，把渲染表面送到眼镜 Presentation 窗口；不需要修改安全设置或开启开发者调试。Server 1.0.1 另外补上设备控制权交接，已通过物理重连后首次 AR 启动测试，修复旧版首次控制通道 EBUSY 的问题。

## 开始使用

1. 手动传输并安装稳屏 APK，接上 Air 2 Ultra。
2. 打开 **XREAL Server**，启动完整服务，等到 `READY`，切换 **立体 3840×1080 / 60Hz**。
3. 从手机桌面打开 **XREAL 稳屏**，点击 **启动眼镜画面**。
4. 眼镜画面启动后会自动回到手机控制页，点击 **允许整屏投射**，在系统提示中同意共享整个屏幕。若系统提供“单个应用 / 整个屏幕”选项，选择整个屏幕。
5. 点击 **返回手机桌面**，正常打开浏览器、相册等 APP。眼镜应持续显示手机主屏幕，转头时虚拟屏幕保持在原来的方向。

不需要手机连接电脑或开启调试。不需要给其他 APP 加入 XREAL SDK，也不需要 ROOT 或无障碍点击权限。稳屏仍依赖 Server 提供眼镜运行时。

## 控制

- **屏幕居中**：点击后有 1 秒准备时间，看向希望放置屏幕的位置。通知栏也有居中按钮。
- **屏幕大小**：60%～180%。默认同时适配横屏和竖屏；放大后部分内容可能超出眼镜视野，可转头查看或缩小。
- **3DoF / 6DoF**：默认 3DoF 固定方向。切到 6DoF 后屏幕固定在真实空间位置，头部左右平移和前后移动时应有视差；切换后自动重新放置屏幕。仅使用 SDK 返回的真实位置，定位丢失时显示等待状态。6DoF 依赖眼镜与环境的真实定位能力，宜在光线合适、纹理丰富的环境测试。
- **空间悬停**：默认开启。关闭后屏幕跟随视线移动，用于对照。
- **细小抖动过滤（实验）**：默认原生固定锚点，完全绕过内容补偿。实验轻微 / 均衡 / 较强会修正内容位置和方向，可能增加拖尾或与原生重投影产生时序偏差。升级会回到推荐默认，旧版滤波偏好不会自动启用。实验过滤每个渲染帧只推进一次。
- **静态标定网格**：使用与真实投屏相同的姿态、固定锚点和显示窗口，停止手机采集与纹理上传；可区分定位 / 输出问题与投屏负载。关掉网格后需要重新允许整屏投射。
- **投屏质量**：节能 960 像素 / 30 fps，流畅 1200 / 60 fps（默认），清晰 1600 / 60 fps。像素指最长边，帧率是目标上限。实际取决于源 APP、系统、纹理上传和眼镜刷新率。当前立体眼镜模式为 60Hz，不承诺 90/120Hz。
- **测试投屏流畅度**：显示持续变化的网格和移动色条，同时显示手机动画、手机画面上传和眼镜渲染帧率。先开启整屏投射，再打开此页；静止桌面不适合测量最高投屏帧率。
- **重新同步画面**：重建采集表面并接回当前采集会话。遇到残缺画面可手动恢复；新旋转路径本身不再随横竖屏变化重建或缩放采集表面。
- **画面上下校正**：默认开启，匹配 Android 图像与 Unity 纹理的方向；若设备显示倒置可切换。
- **停止投屏**：控制页、通知栏或系统共享状态入口均可停止。停止后清除眼镜里的上一帧画面。
- **退出稳屏**：停止采集并结束该 APP 的 XR 客户端；不会停止共享的 Server。

## 系统边界

- 每次新采集会话都需要系统授权；不会保存授权令牌或在重启后静默开始录屏。
- 设置了 `FLAG_SECURE`、DRM 或其他系统保护的区域可能黑屏。普通应用无法保证所有受保护页面都可被采集。
- Android 15 QPR1 及之后的系统通常会在锁屏时终止共享。解锁后重新开启并授权即可。
- 当前仅投射画面，声音继续使用手机原有输出。没有录制视频、采集麦克风或上传画面的实现。
- 采集最长边上限 1600 像素、最高目标 60 fps；头部姿态和眼镜渲染独立更新。静止源画面可能很少产生新帧，系统帧率提示也不能强制其他 APP 提高刷新率。

上述系统行为参考 [Android MediaProjection](https://developer.android.com/media/grow/media-projection)、[Android 15 共享状态与锁屏停止说明](https://developer.android.com/about/versions/15/behavior-changes-all)。外屏窗口使用 [Android Presentation](https://developer.android.com/reference/android/app/Presentation)，本应用单独关闭 SDK 的 Support Multi Resume。

## 首次验收

按顺序测试：桌面 → 打开普通 APP → APP 内滚动 → 连续横竖屏切换 → 3DoF 转头观察 → 切到 6DoF 并左右平移 / 前后移动 → 居中 → 停止投屏 → 重新授权启动。最后测试拔掉眼镜是否停止采集。

6DoF 验收应保持朝向基本不变、平移头部约 20～30 cm，确认屏幕仍停在原来的空间位置，并确认诊断中 `actualTrackingMode=6`、`positionTracked=true`。仅转头不够区分 3DoF 和 6DoF。

防抖验收在同一个位置依次切换关闭、均衡、较强，比较看文字时的微抖与快速转头的拖尾。帧率验收用动态测试页比较节能与流畅档，查看 `sourceFrameFps`、`acceptedFrameFps`、`uploadedFrameFps`，不要只看 `renderFps`。连续旋转后检查整个桌面四边都完整显示，必要时点重新同步画面并复制诊断。

从手机切换到其他 APP 后，眼镜画面与姿态必须继续更新；只在控制页中工作不算完成整屏稳屏验收。静止桌面的采集 fps 可能降低，需结合滚动页面观察。

如果黑屏或停止更新，回到稳屏控制页点击 **复制诊断信息**。报告包含请求 / 实际跟踪模式、真实位置是否有效、防抖修正角度 / 毫米、源帧率 / 采集帧率 / 上传帧率 / 渲染帧率、CPU 拷贝与纹理上传耗时、手机尺寸 / 固定采集表面尺寸、服务状态和启动日志，不包含手机画面。过滤修正量不是实测抖动改善率。

如果启动后仍黑屏，请等待约 20 秒让控制页返回；若系统没有自动返回，可直接点桌面的“XREAL 稳屏”图标。新版报告包含 `displayPath`、`presentationReady`、`presentationDisplay`、`presentationWidth`、`presentationHeight`、`displayStartAllowed`、`systemMirrorSetting`、分阶段启动记录及本应用进程日志，帮助分辨是尚未创建 Unity Activity、显示页没有正常启动，还是 Unity 内部初始化失败。Presentation 路径下 displayStartAllowed=false 表示系统仍禁止外屏 Activity，不代表本应用的 Presentation 失败。不要通过反复点击“启动”或直接开启采集来判断渲染端已正常运行。

## 源码与构建

Unity 菜单：`XREAL → Build Stable Phone Screen APK`。构建前运行 `ServerAppResearch/prepare_screen_stabilizer.py`。该步骤仅生成本 APP 使用的显示器识别兼容 AAR，不修改原 AAR。SDK 托管手部诊断宏仅由单独的 RuntimeAudit Demo 构建启用，稳屏不启用该实验。

- Unity 场景及逻辑：`Assets/ScreenStabilizer/`。
- Android 控制页、系统授权、采集服务：`Assets/ScreenStabilizer/Plugins/ScreenMirror.androidlib/`。
- 构建入口：`Assets/Editor/XREALScreenStabilizerBuild.cs`。
- 离线核验：`ServerAppResearch/verify_stable_screen.py`。

构建以 3DoF 启动，可运行时切换 6DoF，使用无手柄输入及 Presentation 渲染路径，结束后恢复项目原设置。可见的 Presentation 维持 Unity 渲染；锁屏和拔掉眼镜时会暂停。屏幕图像作为世界坐标中的纹理面板，头部姿态在渲染前更新，不修改手机系统桌面合成器。固定正方形采集表面使用 Android 12L+ 的等比例居中缩放行为，裁出真实手机内容后以连续 RGBA 内存上传；普通旋转只更新裁切区域。

SDK 3.1 随包公开 C# API 中未找到可调的头部微抖滤波接口，因此新版采用自行实现的自适应四元数 / 位置低通。原生预测与重投影仍由 SDK 处理。设计参考 [1€ 滤波原作者的调参思路](https://gery.casiez.net/1euro/)，具体实现和离线数学测试见 `StablePoseFilter.cs` 与 `StableScreenMotionChecks.cs`。

0.3.1 每 5 秒在后台记录帧间隔、可用的 CPU / GPU / XR 计时、GC、进程内存和系统温控状态，最多三个 2 MiB 文件：`Android/data/dev.xreal.stablescreen/files/runtime-health/`。不记录画面。SDK 不支持的指标为 -1；复制诊断时带最近记录，之后连接调试可读取历史。性能记录本身不能证明温控或内存泄漏是故障原因。
