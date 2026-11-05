# 稳屏与 6DoF Demo 的输出链路对照

2026-11-03，实机 Xiaomi 15 / Android 17 API 37 / Air 2 Ultra。
稳屏 0.3.2 使用静态网格，采集关闭、实际 6DoF、默认原生固定锚点；
对照为截图 Demo 1.0.5。Demo 的手势轮询计数在稳定窗口中不再增长。
本轮没有修改姿态、滤波参数、手机显示设置或安装应用。

## 已测得的差异

| 项目 | 稳屏网格 | 截图 Demo |
|---|---|---|
| Unity 更新 | 约 59～60 fps | 约 59～60 fps |
| 眼镜表面所有者 | 自建 Presentation，`UnityPlayer.displayChanged(0, surface)` 接管主输出 | SDK 创建的独立眼镜 Surface，原生 Rendering 输出 |
| `NativeRendering FrameWait` | 20:46:59.167～20:47:19.216 的日志中持续 1196 次，约每帧一次 | 启动 20:54:38.353～38.788 共 27 次；后续数分钟窗口未再出现 |
| SDK 原生指标 | `DisplayRefreshRate: 0`，`FramePresent: 0` | `DisplayRefreshRate: 60`，`FramePresent` 有非零值 |
| SF 眼镜逐帧记录 | 每次快照 126～127 条；约两秒窗口出现 2～3 个约 33ms 的呈现间隔 | 无有效逐帧队列记录；日志明确 `single_buffer:1` |

稳屏的六次 SF 快照相邻约 2.1 秒。单次约两秒的统计区间略有重叠，
不能将快照简单拼接成独立帧总数。稳屏的生产/就绪间隔大多约 16.8ms，
实际呈现间隔大多约 16.45ms，间或约 33ms。这与不均匀呈现相符，
但不足以单独解释眼镜内全部主观现象。

Demo 的手机 Unity 镜像也能读到约 33ms 的 SF 间隔；它不是眼镜输出，
不能用作 Demo 眼镜帧率。SDK 眼镜层在两次 SF dump 中仍显示同一个
buffer/frame=1，同时用户能看到实时头部跟踪内容。这和日志中的单缓冲
配置相符。SF 的普通 buffer 队列时间戳无法测量这条路径的实际光学刷新。

`FramePresent`、`DroppedFrame` 是 SDK provider 字段，本轮没有建立其完整
统计定义，不能把 1 当成 1 fps，也不能把变化量直接当作累计丢帧。

## 目前能够支持的判断

“Demo 因为 Unity 渲染帧率更高所以更顺滑”没有得到数据支持。
两者的平均更新频率接近，Demo 部分 Update 最大间隔甚至更长。

当前最明确、值得优先控制验证的变量是输出路径：稳屏仍在直接显示
Unity 主画面，而 SDK 原生 Rendering 的 FrameWait 持续失败；Demo 的
原生眼镜显示初始化后恢复正常。尚不能仅凭这些日志断言特定的
预测、重投影或时间扭曲功能已经在某个应用中启用/停用，更不能宣称已修复。

静态网格仍不连续使问题不再依赖 MediaProjection、JNI 像素拷贝或实时
纹理上传，但不能据此排除姿态采样、相机变换和呈现节奏的其他问题。

## 下一步

1. 保存稳屏一次完整冷启动日志，查清 SDK 的眼镜 Surface 创建失败。
   旧版 0.2.0 日志已有 `Catch exception of display`，仍需当前 0.3.2 的原因。
2. 用与 Demo 相同的原生眼镜输出做对照，保持原始相机、固定锚点和 60Hz。
   不再通过增加滤波来掩盖输出停顿。保留可恢复的 0.3.2 APK。
3. 实机必须确认原生 Surface 成功、FrameWait 不再持续失败、静态网格的
   连续运动和切换手机 App 后的后台输出，再判断是否解决。

数据汇总：`../evidence/display-cadence-comparison.json`。
重算：`python ServerAppResearch/analyze_display_cadence.py`。

## 设备充电期间的离线补查

- 已核对 Server 1.0.1 APK 内 `libnr_api.so` 与发布 SDK 的 `nr_api.aar`
  内库完全相同：38,054,896 字节，SHA-256
  `f4af353cfec82671f863ba9bda315d7ad538b206559f63f21127ee1ea244a830`。
  两份客户端 APK 自身都不打包这份库，使用动态加载的运行时。
  两份客户端已有原生库（排除 Unity/IL2CPP/Burst 生成部分）逐个相同。
  不能将区别归因于两份客户端使用了不同版本的已打包原生渲染库。
- 两份客户端 DEX 都没有 SDK 的 GlassesDisplay / SurfaceListener / GlassesListener
  定义。Demo 已正常创建眼镜画面，所以仅凭客户端没有这些定义不能断言缺类；
  运行时 Java 类另由 SDK 加载机制提供。
- 原生代码 `0x1314830` 调用 Presentation 构造包装 `0x131c428`，包装缓存
  `android/app/Presentation` 及其 `(Context, Display)` 构造函数。
  离线解码的 `0x465f9f` 标签也为 `android.app.Presentation`。
  **SDK 正常输出同样使用 Presentation。不能把问题归结为 Presentation API 本身。**
  当前待验证的区别是谁持有/呈现眼镜 Surface，以及 SDK 的原生 Rendering
  同步链路是否成功；不是简单的“Presentation 对外屏 Activity”。
- 尚未证明 SDK 依赖 Unity 主 Surface、特定 Activity 字段、硬件加速标志或
  主题导致当前失败；这些不得写成已确定原因。
- `stable-grid-cold-start.log` 实际仅记录了控制页的设备状态轮询，没有眼镜
  启动事件。设备电量耗尽，不能把这份文件当成已完成的冷启动复现。
  等设备可用后由用户手动启动，再采集完整现场。

补充证据：`../evidence/display-native-packaging-comparison.json`、
`../evidence/display-native-decoded-labels.json`。

## 充电后复现：同一眼镜显示器的两个窗口冲突

稳屏 0.3.2，进程 32473，2026-11-03 晚间完整启动记录：

1. 21:44:40.767，自有 `ScreenPlayerActivity` 创建 `presentation-shown display=2`。
2. 21:44:46.977，SDK 开始创建自己的眼镜 Surface。
3. 21:44:46.987，系统 WindowManager 拒绝：
   `Attempted to add presentation window to a non-suitable display. Aborting.`
4. 21:44:46.992，SDK 输出 `Catch exception of display`。
5. 本次保存日志中，没有 SDK `OnDisplaySurfaceCreate` 回调，并有 4163 条
   `NativeRendering FrameWait` 失败。场景和姿态仍继续更新。

从此前从本机提取的 `xiaomi-services.jar` 中单独提取并反编译
`com.android.server.wm.PresentationController`，确认实际规则：
`hasPresentationWindow(displayId) && win != mPresentations.get(displayId).mWin`
时拒绝新窗口。这台系统的实现直接应用这项检查，不能用早期 Android
只检查显示标志的行为判断。AOSP 同类规则亦见
[PresentationController](https://android.googlesource.com/platform/frameworks/base/+/android16-qpr2-release/services/core/java/com/android/server/wm/PresentationController.java)。

因此，原生眼镜输出失败已找到可解释的窗口占用冲突。它是现有输出路径的
确切问题；是否解释并解决用户看到的全部不连续，仍需要消除冲突后的对照。
不再将这次拒绝当作显示器型号未识别，也不尝试改变系统镜像设置。

证据：`../evidence/stable-grid-current-startup.log`、
`../evidence/stable-grid-current-window-errors.log`、
`../evidence/stable-grid-window-conflict.json`、
`../build/mirror-analysis/XiaomiPresentationController.java`。

## 0.4.0 原生输出对照包

- 删除稳屏自建的眼镜 Presentation；SDK 独占眼镜窗口，沿用原生 Rendering。
- Unity 主表面绑定到一个请求为 64×64 的 ImageReader。它没有窗口，后台
  acquireLatestImage 后立即关闭，不拷贝/保存图像。这只是维持 Unity 渲染
  生命周期的无用镜像接收端，和用户授权的手机采集无关；眼镜 XR 纹理尺寸
  仍由 SDK 决定，实际设备行为待验证。
- SDK 提供的刷新率和当前帧呈现次数用于报告输出就绪，再允许申请整屏
  共享。它不是物理刷新率或光学画面验证。字段名为 `sdkOutputReady`、
  `sdkReportedRefreshHz`、`sdkFramePresentCount`。
- 保持相机姿态、锚点、滤波算法、采集策略及 60Hz 目标不变。
- 73 个 SDK/兼容原生库与既有包完全相同；两份 GLES 着色器各有 6 个
  有效程序；签名与 ZIP 对齐通过。还从成品 APK 提取并反编译宿主类，
  验证没有自建 Presentation，且实际包含 ImageReader 的取帧和关闭。

产物：`D:\Xreal\Build\Android\XREAL-StableScreen-v0.4.0.apk`，
130,860,472 字节，SHA-256
`84af95902da358f2c169cee50621c0c8d7b182ad42a1f30ded8588263aa32329`。
Server 无需更新。构建、静态核验均通过；已请用户手动安装，实机改善待验证。

## 0.4.0 实机回退与 0.4.1 持续刷新对照

用户已安装 0.4.0，反馈静态网格实际约 1 fps，但拖动手机上的大小滑块时，
画面与转头马上恢复流畅，停止操作后再次卡顿。实际光学帧率没有仪器测量，
这里的 1 fps 为用户估计，不能与 Unity 更新频率混用。

读取进程 8896 的完整日志确认：

- 22:02:00.298，SDK `OnDisplaySurfaceCreate` 成功。
- `FrameWait` 仅启动阶段 26 次，最后为 22:02:00.295，之后不再持续失败。
- `sdk-rendering-ready=true providerHz=61.0 framePresent=1`，初始化窗口冲突
  已消除，但这些 provider 指标不能证明物理画面连续刷新。
- 稳定窗口 Unity 更新平均 59.64 fps，所有窗口最大间隔不超过约 22.97ms；
  thermalStatus=0，采集关闭。没有证据支持把这次约 1 fps 归为 Unity 主线程
  每秒只运行一帧、投屏纹理上传或已触发系统温控。

“滑块交互时恢复”使优先假设转为：SDK 持续改写单缓冲内容，但独立的
离屏 Unity 主目标不再提供屏幕窗口更新，眼镜呈现依赖手机 UI 触发的合成。
旧控制页约每秒更新一次状态，与停顿频率相符，但尚未用物理呈现测量建立
完整因果关系。一次 SurfaceFlinger 重绘探测被系统权限拒绝，未修改系统设置。

0.4.1 在 SDK **现有眼镜窗口**内部增加一个不接收触控的 2×2 像素深色
绘制区域，每次 onDraw 轻微改变颜色并 postInvalidateOnAnimation，触发
真正的窗口绘制。它不创建新窗口，也不改写 SDK 眼镜 Surface、预测姿态或
原生库。使用公开 WindowInspector 只枚举本进程窗口，并限定眼镜 displayId。
锁屏/暂停时停止，销毁时移除。提供默认开启的“连续刷新眼镜（兼容）”开关，
可在同一安装包里做开启/关闭对照。持续绘制会增加耗电。

诊断新字段 `refreshDriverAttached`、`refreshPulseDrawFps` 表示附加视图状态和
该视图 onDraw 次数，不是实际眼镜光学帧率。SDK 就绪指标仍保留原始含义。

产物：`D:\Xreal\Build\Android\XREAL-StableScreen-v0.4.1.apk`，
130,864,636 字节，SHA-256
`59c5bb4fe7bea40f7a78cfccc19e456222a860455eaba68cd199730aeca8df2d`。
Unity 构建、Java 编译、签名、ZIP 对齐、两份 GLES 各 6 个程序、成品宿主和
刷新驱动反编译核验通过；73 个 SDK/兼容原生库仍保持一致。
**0.4.1 的实际流畅性尚未验证，等待用户手动安装。**

设备证据：`../evidence/stable-v040-stutter.log`、
`../evidence/stable-v040-threads.txt`、`../evidence/stable-v040-surfaceflinger.txt`、
`../evidence/stable-v040-stutter-analysis.json`。
产物核验：`../evidence/stable-screen-apk-verification-v041.json`。

## 0.4.1 实机开关与跨 APP 验收

2026-11-03，用户手动覆盖安装 0.4.1 后明确反馈：

- 连续刷新开启：静态网格非常流畅；关闭：非常卡顿。
- 关闭连续刷新时，普通手机滑动不能让眼镜画面刷新。
- 连续刷新保持开启，开启整屏投射后回到桌面、停止触摸至少 10 秒再转头，
  并切换普通 APP：桌面和其他 APP 均流畅。

因此，当前设备上“持续刷新开关开启”的兼容路径已通过用户视觉验收，
可以保留 0.4.1，不需要为此次问题继续调整姿态滤波。

需修正前面的初步表述：调整大小会发送 Unity 设置命令并改变眼镜内容，
普通手机滚动则不一定如此。不能从早先滑块恢复现象推断任意手机 UI 重绘
都会驱动眼镜。已验证的因果对照是**眼镜现有窗口的独立持续绘制开关**，
并非“全局手机画面活动”或某个尚未证实的硬件缓存实现细节。

验收来源为用户明确反馈；光学帧率、完整光子延迟、长时间耗电/发热尚未测量，
不把 Unity 约 60 fps 或脉冲视图绘制次数写成物理刷新率。
本次不再更改 APK，保留已验证的文件和开关默认值。
