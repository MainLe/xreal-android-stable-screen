# 0.6.1 手势触控修复

2026-11-04，Xiaomi 15 / Android 17 API 37 / Air 2 Ultra，无线端口 40153。

## 实机诊断边界

安装包 SHA 与 0.6.0 构建成品一致。起初系统未启用 PhoneGestureService；用户开启后，
dumpsys accessibility 确认该服务已绑定、capabilities=32、没有 crashed service。
用户随后反馈：快速点击也表现为长按、不能拖动，但张开时球立即变青，捏住移动时球仍移动。
这说明手部识别与 Unity 按下/松开状态在更新，后续触控注入需要独立检查。
用户同时确认 6DoF CubeInteraction 1.1.0 没有问题。

## 旧版代码缺陷与回归

旧服务在每次 onCompleted 后无条件提交下一段 60ms willContinue 笔画，包括原地不动的路径。
AOSP MotionEventInjector 只在坐标改变时生成 MOVE；continued + stationary + willContinue
既没有 DOWN，也没有 MOVE/UP，events.isEmpty() 会通知失败，而旧的已按下指针可能仍然存在。
因此不能把 onCompleted 理解为“等到了标称 duration”，也不能在失败后只丢弃 stroke 引用。

公开参考源码：
[MotionEventInjector](https://android.googlesource.com/platform/frameworks/base/+/30e2ac44bff2/services/accessibility/java/com/android/server/accessibility/MotionEventInjector.java)，
特别是空事件失败、appendMoveEventIfNeeded 与按最后事件回调的处理；
[GestureDescription](https://android.googlesource.com/platform/frameworks/base/+/HEAD/core/java/android/accessibilityservice/GestureDescription.java)
也会将触控坐标取整。

构建完成后，进一步从已有本机系统副本 `build/mirror-analysis/xiaomi-services.jar` 提取
`com.android.server.accessibility.MotionEventInjector`，反编译结果保存在
`build/cadence-native/XiaomiMotionEventInjector.java`。本地副本与手机当前 `/system/framework/services.jar`
SHA-256 完全一致：`e67000c7f004e5590ba923511473ce1812f0470294c573c9c3eb2df7490c9015`。
其中第 169～171 行同样在空事件时通知失败并返回；appendMoveEventIfNeeded 也只在坐标改变时生成 MOVE。
哈希比对证据为 `evidence/stable-gesture-system-framework-match.json`。因此上述机制已核对本机实际系统代码，
不只是依据其他 Android 版本作推测；本次旧 APK 的逐事件时序仍未直接捕获。

原测试适配层错误地总等待标称时长并总返回成功，没有复现 Android 的空事件行为。
本次改为事件推导的适配层后，原 0.6.0 服务在快速点击用例失败：
`AssertionError: contact did not terminate with actual UP`。

## 修正

- 原地按住保留现有 stroke，收到真实位移或松手时才续接；不提交空的继续笔画。
- 坐标先量化到整像素，再判断位移，避免亚像素路径经 Android 取整后仍为空。
- 50ms 独立看门狗负责 350ms 输入过期、15 秒单次上限、几何变化和捕获/授权状态失效。
  即使原地按住没有回调，也会执行抬手。
- 真实移动笔画 24ms、最终抬手 1ms。已有笔画执行期间缓存最新目标，完成后续接；没有重建 DOWN。
- 旧射线用拇指与食指中点瞄准，捏合会改变几何射线。稳屏现在使用 Palm 关节（缺失时 Wrist），
  捏合上升沿保留最近 150ms 内的悬停目标，此后按手掌位移连续拖动；捏合期间固定射线原点。
  张开帧沿用最后按住位置发送 UP。Demo 仍使用原来的捏合点，不改变立方体交互行为。
- 未连接无障碍服务时，“启用手势操控”会明确提示。不会只打开指示球却静默丢弃手机输入。
- 新增非导出 PhoneInputTestActivity：点击/长按计数、滑条、可滚动列表、实际收到的 MotionEvent
  及持续时间。它只记录本测试页收到的事件。StablePhoneInput 日志记录注入状态，StablePhoneProbe
  日志记录测试页实际事件，移动日志各最多约 4 次/秒。

## 验证与未完成项

Java 回归通过：快速点击真实 UP、静止长按不续接空路径、连续拖动、亚像素量化、原地看门狗、
屏幕旋转/采集失效、移动中释放、系统取消后的重新准备、无效坐标与导航互斥。
Unity 构建同时检查捏合目标保持、手掌拖动、张开位置保持、过期悬停与重置。
连续刷新、SDK 原生输出、3/6DoF 姿态和手机投屏路径保持原实现。

0.6.1 需要手动覆盖安装后，在测试页验证真实 Android DOWN/MOVE/UP 与按钮/滑条/列表响应，
通过后再验证桌面和其他 APP。离线回归不能代替本机系统输入分发验证。

构建成品：130,902,904 字节，versionCode 11。
SHA-256：`ceab26d086f7ca3b6c349fdabad74021ccbcddc55f0d63bc6f4a12284c29a37a`。
签名、ZIP 对齐、GLES 着色器、实际打包 Java 实现检查通过；73 个 SDK/兼容原生库保持一致。

## 当日实机结果

用户安装 0.6.1 后明确反馈“已验证可以正常操控”。无线日志捕获 38 次按下与 38 次抬起完成，
有连续拖动坐标更新；本段没有系统取消或注入拒绝。完成触控时长范围 181～3689ms，
包含短按释放和较长的持续接触。安装包 SHA 与上述成品完全一致。
本段日志没有 StablePhoneProbe 条目，因此不宣称已独立统计测试页实际 MotionEvent，
也不将用户总体通过反馈扩展为每个交互模式的单独验收。
验证记录：`evidence/stable-screen-v061-device-validation.json`。
