# 手势手机输入与无重力立方体交互

2026-11-03，新功能离线开发；设备暂不可调试。

稳屏 0.5.0 使用已验证的 0.4.1 显示链路：SDK 独占眼镜 Presentation、离屏 Unity 主表面、
现有窗口内持续刷新。手部输入复用既有 XREAL CPU 图兼容 shim 和 DSP 搜索路径准备，
不更换官方原生库、不宣称 DSP 已可用。手部读取诊断宏仍只用于 Demo。

`HandPhoneControl` 在 LateUpdate 从真实 XR Hands 获取所选手的有效手腕和拇食指中点，
将肩部参考射线与实际手机 quad 平面相交，再按材质上下翻转映射到默认显示器归一化坐标。
手势操控每次会话默认关闭；选手、停止、投屏、定位和 SDK 输出就绪均参与门控。
光标和导航标签在 XR 场景中绘制，不覆盖或监听手机其他 APP。

`PhoneGestureService` 通过 Android 用户手动启用的 AccessibilityService 发送主动触控。
服务具备 canPerformGestures，关闭 canRetrieveWindowContent；事件回调不读取 UI 信息。
用 60ms `StrokeDescription.continueStroke` 续接同一接触，轨迹始点严格等于上一段终点，
只保留最新手势位置，不排队积累未来路径。起始点周围使用系统 touch slop，方便长按。
手部消息超过 350ms、手机旋转、采集几何改变、停止投屏、锁屏或 15 秒超时均抬起已有接触。
系统取消后等待捏合释放再重试。已有触控的释放可能产生目标应用正常的抬起行为，不能保证
撤销之前已经发送的点按、长按或拖动；不伪称能回滚 Android 输入。

导航是明确瞄准 BACK/HOME/APPS 并捏合松开触发的 global action，不自行推断界面目标。
参考 [dispatchGesture](https://developer.android.com/reference/android/accessibilityservice/AccessibilityService#dispatchGesture(android.accessibilityservice.GestureDescription,%20android.accessibilityservice.AccessibilityService.GestureResultCallback,%20android.os.Handler))、
[continueStroke](https://developer.android.com/reference/android/accessibilityservice/GestureDescription.StrokeDescription#continueStroke(android.graphics.Path,%20long,%20long,%20boolean))。

Demo 1.1.0 的 `InertialCubeInteraction` 在 LateUpdate 读取同一有效手部采样。
单手抓取保存手腕旋转下的物体局部偏移与旋转，双手使用连线和手腕上方向构造旋转参考系；
从单手转双手或反向转换时重绑偏移，避免跳变。无效的双手参考系回退单手。
张掌靠近并向物体运动时加入有限推动冲量。松手使用经过速度上限和时间有效性检查的
平移/旋转速度；跟踪丢失则清零，避免将重定位跳变视为投掷。

惯性按指数阻尼解析积分：`delta = v*(1-exp(-k*dt))/k`，`v *= exp(-k*dt)`。
线性 k=2.2，角速度 k=2.8；低于阈值后归零。无 Rigidbody、重力或环境碰撞。

验证脚本：`test_phone_gesture_service.py`（实际 Java 状态机、模拟 Android 适配层）、
`Assets/Editor/HandInteractionChecks.cs`（Unity 数学检查）、`verify_stable_screen.py`、
`verify_cube_interaction.py`（成品校验）。设备安装、物理手势精度、SDK 推理负载及真实触控
表现仍待稍后验证。

最终两份 APK 已构建并通过成品核验，见 `../evidence/hand-phone-cube-build-summary.json`。
Java 手势状态机适配层检查和 Unity 数学检查均已通过，本轮无设备连接或安装。
