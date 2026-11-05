# 0.8.0 选择真实平面并固定屏幕

用户要求选择一个平面，将手机屏幕 pin 到该平面上。实现覆盖水平/垂直平面选择、预览、确认、
空间锚点保持、解除固定，并与已有左手持屏互斥。仅本次运行，不自动保存/加载跨重启地图。

## SDK 与坐标

本地 XREALPlaneSubsystem 声明支持水平、垂直平面及 boundary vertices；XREALAnchorSubsystem
声明 supportsTrackableAttachments=false。因此没有调用不受支持的 AttachAnchor(plane, pose)。
在选中的真实平面位置创建独立 ARAnchor，使用其跟踪位姿保持屏幕；创建失败不会降级为普通 Transform 后声称固定成功。
SDK Provider.Start 自动配置持久目录，当前只使用会话锚点，不请求保存映射。

稳屏场景加入 identity XROrigin、零 Camera Offset，手关节与头部相机共享该坐标空间；
ARPlaneManager 和 ARAnchorManager 默认关闭。既有相机姿态仍由 XrHeadPoseSample 驱动，
SDK 独占眼镜输出及连续刷新路径不变，没有新增 Android Presentation。

## 选择与朝向

进入扫描自动请求 6DoF，等待真实位置跟踪有效后开启平面检测。默认用头部相机前向射线，
可选食指射线（沿头部到食指的方向，从指尖向外延伸）。
PlanePinMath 在 ARPlane 的 local XZ boundary polygon 内求最近命中，拒绝仅落入 bounding box 的区域，
支持凹边界并排除退化/非有限多边形、平行/背向/超过 8m 的命中。
最多绘制 32 个平面边界，边界更新间隔 0.25s，黄色矩形预览屏幕姿态与比例。

屏幕平行于平面，朝向初始观看者并沿外法线偏移 1cm。墙面保持世界竖直方向为顶部，
桌面使用观看方向在平面内的投影确定顶部，直视下方时有非退化备用轴。
预览/放置时参考距离取头部到位置的距离，限制 .25～5m，保留手机图像宽高比；固定后可缩放。

先观察到张开，再捏合才确认；跟踪恢复或一直保持捏合不会自动确认。也可用手机的 2s 延时按钮。
倒计时中失去真实 6DoF 或更换所选手/瞄准方式会取消延时确认。
选择期间隐藏手机图像并禁止手机输入；手部跟踪可用于选择，不依赖无障碍授权。

## 锚点与模式

确认只接受当前仍 Tracking、未被合并的 ARPlane，以及正在运行的 ARAnchor 子系统。
通过 ARAnchor 注册原生会话锚点，必须得到有效 trackableId；最多等待 5s 进入 Tracking，
失败后报告原因并返回选择，不使用固定的世界矩阵冒充原生成功。
固定成功后停止平面检测、清除扫描边界，只保留一个锚点。
若锚点或真实 6DoF 失效，隐藏图像、暂停输入并等候恢复；失效锚点可以手动重选。

PlaneScreenPin LateUpdate 顺序 50：在 StabilizedScreen 的头部/左手更新之后、HandPhoneControl
顺序 100 之前设置当帧屏幕矩阵。平面放置有独立呈现/输入状态，BeforeRender 不以普通居中、
视线跟随或内容补偿覆盖锚点。放置阶段变化和锚点跟踪状态变化递增输入几何版本，取消已有接触，
确认捏合也不会在新屏幕上转变为手机点击。

Java 追加平面请求/阶段/候选/计数/锚点 ID/输入就绪等诊断，并在选择或锚点未就绪时保护 phoneInputAllowed。
启用左手绑定会清除平面放置；进入平面放置会关闭左手绑定。固定期间不允许切 3DoF，
距离与视线跟随控件暂停，解除后返回普通观看。锚点销毁后延迟停用 manager，留出原生移除时机。

## 验证与边界

PlanePinChecks 检查真实多边形/凹区域、边缘、无效边界、受限射线、任意坐标变换、墙/桌面
位置朝向和表面偏移、正反法线一致、直视下方不退化、捏合松开/恢复状态。
构建检查 XR Origin identity、相机/手部共用空间、扫描模块默认关闭；既有触屏/掌面/注入检查继续运行。
这些不代替原生设备测试；实际 SDK 平面输出、锚点注册和持续跟踪仍待用户安装确认。

成品：`Build/Android/XREAL-StableScreen-v0.8.0.apk`，versionCode 16，130,936,992 字节。
SHA-256：`565a0be309d5b001e9f961a7ccb980970948cc74c1dc5661652fdc3b68c3a622`。
签名、ZIP 对齐、三份 GLES 着色器及打包实现检查通过；73 个 SDK/兼容原生库保持一致。
PhoneGestureService 和 SdkRefreshDriver 源哈希与 0.7.3 一致。
