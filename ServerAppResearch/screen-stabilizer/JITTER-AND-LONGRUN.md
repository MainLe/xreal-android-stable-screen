# 稳屏微抖与空间 Demo 长时间运行调查

日期：2026-11-03。用户反馈：稳屏 0.3.0 的 3DoF、6DoF 都存在小幅抖动；此前空间 Demo 初始较稳定，但开启手势后偶发高延迟、抖动、撕裂，出现时间不固定。此次未连接手机；以下区分源码证据、已修复代码缺陷与待验证推测。

## 实现差异

| 环节 | 稳屏 0.3.0 | 空间 Demo 1.0.3 | 本次处理 |
|---|---|---|---|
| 相机姿态 | XRNode.Head，在 Update 和 BeforeRender 读取 | 同一来源和两阶段更新 | 使用同一个 XrHeadPoseSample，保持 SDK 原始姿态 |
| 跟踪有效性 | 有 trackingState 时，没有同时要求 isTracked=true | 同时要求 isTracked 与对应标志有效 | 统一为严格有效性，拒绝失效状态的遗留标志 |
| 世界内容 | 默认均衡滤波，每次采样后反向修正内容位置和方向 | 世界标记不随头部采样改写 | 新默认直接使用固定锚点；实验补偿仍可手动打开 |
| 滤波时机 | 一帧可能推进两次，且两次间隔取决于 CPU 工作量 | 没有这层滤波 | 实验滤波只在 BeforeRender 每帧推进一次 |
| 显示窗口 | 自建 Presentation，接入 Unity 主 Surface | SDK 默认 Unity Activity / 原生显示路径 | 保留已能工作的路径；这项区别尚不能离线排除 |
| 图像工作量 | MediaProjection → RGBA 拷贝 → JNI → Texture2D.Apply | 原生绘制简单几何，无投屏上传 | 加静态网格对照，保持同一相机、锚点、窗口，但去掉采集与上传 |
| 跟踪启动 | 先 3DoF，再按设置切换 6DoF | 直接 6DoF，含 XROrigin | 保留行为，报告实际模式；不能把 3DoF 与 6DoF 的差异当成纯应用差异 |

同样 60 fps 的平均帧率并不保证帧间隔一致，也不证明屏幕扫描输出没有撕裂。微抖还可能同时来自姿态噪声、渲染时序或投屏负载，不能仅凭滤波强度判定。

另有图像采样差异：稳屏使用无 mipmap 的 RGBA 纹理和双线性过滤，显示大量缩小的手机细字；Demo 主要是几何标记。细字采样闪动的可见程度不能直接与大方块比较。静态网格与手机内容也不同，因此“网格更稳定”不能单独证明 JNI 或上传就是原因。本次保留原纹理策略，避免在没有设备数据时再引入每帧生成 mipmap 的额外开销；后续需要结合静止手机页面、真实采集帧率和姿态数据区分图像采样与定位抖动。

## 可复现代码缺陷：关闭滤波仍复用旧姿态

StablePoseFilter.Step 旧顺序为：先判断 dt<1ms，直接返回 filtered；之后才处理 strength=0。因而 Update 后 100 微秒到来的 BeforeRender 新样本，在“关闭”档也可能返回上一样本。随后 Compensate 用新 raw 和旧 filtered 移动世界屏幕。关闭过滤实际并非完全透明。

已将关闭档提前无条件返回当前样本，并让默认渲染直接使用逻辑世界锚点，完全绕过补偿计算。新增 Unity 回归测试以 100 微秒间隔、不同位置和四元数验证关闭档确实返回新样本；另验证 isTracked=false 时，即使 Rotation/Position 标志保留也不会继续接受姿态。

升级后使用新的滤波设置键，默认“原生固定锚点”。旧版均衡设置保存在旧键中，但不会自动继续启用实验补偿。实验滤波不是 SDK 原生时间扭曲的替代品；其视觉效果仍待设备比较。

## 开启手势后长时间恶化

以下是已确认的实现特征，不等于已证明此次设备故障原因：

1. SDK 的 XR Hands provider 在 Dynamic、BeforeRender 都同步调用 UpdateHandPose，并读取左右关节。XR Hands 1.3 的两阶段设计本身正常；如果 SDK 同步读取偶发变慢，放在 BeforeRender 的工作可能推迟这一帧。旧代码没有这些调用的耗时数据。
2. 旧 Demo 在手势算法回调报错后，仅隐藏骨架，没有调用 SetInputSource(None)。已改为在 Unity 主线程下一次安全的 Update 停止原生手势输入，保留错误，等待用户手动重试。看不到手、关节暂时无效不会触发这一停止逻辑。
3. 手势兼容 shim 在场景启动就启动轮询线程：目标库不存在时每 5ms 枚举已加载库，存在后每 250ms 检查。现在只在实际请求手势时启动。启用过手势后该线程仍会保留到进程结束；本次没有修改其 .so。
4. 旧日志曾出现高频 GetHandData error:1，以及 SNPE / DSP 兼容问题；它们来自此前手势调查，不是本次长期故障的现场证据。当前 CPU 图配置为单个执行线程、max_queue_size=10，检测输入使用固定长度队列。该配置说明存在有限的内部排队可能，不证明当前积压，也不是无限队列泄漏的证据。
5. 自有事件队列、Server 日志、投屏帧 mailbox 分别有固定上限。未发现这些自有容器无上限增长。SLAM / 手势原生内部资源及系统合成器状态仍需现场数据。

优先待验证的原因：手势原生同步读取卡顿；CPU 推理或温控导致与 SLAM / 渲染争用；原生资源增长；SDK 与 Android 17 的输出同步问题。当前没有足够数据将其确定为温控、内存泄漏或 Server 故障。

## 诊断构建

- 稳屏：`Build/Android/XREAL-StableScreen-v0.3.1.apk`。
- 空间 Demo：`Build/Android/XREAL-RuntimeAudit-v1.0.4.apk`，同包名，可覆盖已有空间 Demo。Server 1.0.1 不需要更新。
- 两者复用 `Assets/XrealCommon/` 的姿态读取和性能记录。
- 稳屏新增“静态标定网格”：停止采集，绘制本地固定网格，排除手机投屏拷贝、JNI 和纹理上传，但保留 Presentation 路径。
- Demo 新增“静态追踪基线”：关闭手部与环境模块，隐藏 HUD，停止方块动画，保留锚点和地图；“恢复 HUD”不会自动重启其他模块。
- Demo 的实验手部读取开关默认关闭，保留 SDK 两阶段读取。开启后 BeforeRender 复用本帧 Dynamic 数据，以减少同步读取；它不会降低原生模型推理帧率，也不证明 DSP 已启用。可能牺牲少量手部显示时效，专用于对照。

### SDK 修改范围

`package/Runtime/Scripts/Android/XREALHandSubsystem.cs` 增加由 `XREAL_HAND_RUNTIME_AUDIT` 控制的托管诊断：累计调用次数、无有效关节次数、原生读取返回 false 次数、BeforeRender 次数、总耗时与最长耗时；以及可关闭的同帧复用。该宏只在 RuntimeAudit 构建中启用。没有启用该宏的构建保留原始读取逻辑。原文件备份位于 `ServerAppResearch/build/runtime-audit/XREALHandSubsystem.original.cs`，核验 JSON 记录前后散列。全部官方原生库、模型、手势兼容 .so 保持原样。

## 不连接电脑也会记录的证据

每 5 秒记录一个窗口：Update 帧间隔平均 / P95 / 最大值、慢帧数、可用时的 CPU / GPU 计时、SDK 目标刷新率、可用时的 XR 掉帧 / 呈现计数、GC、Unity 托管与总分配、原生堆、进程 PSS、线程 / FD 数、系统 thermalStatus 与电源模式、显示模式。Demo 另记录手部原生读取指标。真实长卡顿不会被当成暂停过滤掉；只有明确生命周期暂停才重置窗口。

PSS / 线程 / FD 每 30 秒采样；写文件和系统查询在后台工作线程进行，新记录合并而不积累队列。三个文件各最大 2 MiB，循环使用。位置：

`/sdcard/Android/data/<包名>/files/runtime-health/health-0.jsonl`（最新）、`health-1.jsonl`、`health-2.jsonl`。

记录仅含性能和状态，不含画面。复制诊断时包含最近状态；恢复无线调试后可读取历史文件。API 不支持的 GPU / XR 呈现指标标为 -1；电池温度不是 SoC 温度；Update fps 不是测得的眼镜物理扫描帧率；mailboxAgeMs 仅为采集回调至 Unity 取帧的队列时间，不是完整光子延迟。没有手部关节结果也不自动等同于算法初始化失败。

## 下次复现时的顺序

1. 冷启动 Demo，不开手势；记录稳定基线。再开启手势，保持实际使用到出现问题。
2. 出现问题后先保存 / 复制诊断，再关闭手势，观察是否马上恢复。若有强烈不适可直接关闭；不要求继续佩戴。
3. 再次开启手势，切换一次实验读取模式，观察原生同步读取耗时、帧间隔和现象是否共同变化。手势推理仍持续，因此可以与“关闭整个手势模块”区分。
4. 若两种读取模式均恶化，结合 thermalStatus、CPU / GPU、PSS / 原生堆增长与 SDK 错误定位；需要时再用系统 trace 确认合成和同步时序。
5. 稳屏使用原生固定锚点，比较静态网格与实际投屏，再对比 30 / 60 fps 档位。两种跟踪模式分别记录，不混用结论。

此次已完成离线源码比较、可复现逻辑修复及诊断实现；不能据此宣称现场微抖、长期高延迟或撕裂已完全解决。

最终离线核验：两份 APK 均通过 Unity 构建、签名与 ZIP 对齐检查；稳屏还通过 100 微秒关闭档回归、严格跟踪有效性、滤波 / 坐标数学及三缓冲并发测试。RuntimeAudit 的托管读取探针已确认编入，稳屏确认未编入该探针。官方原生库与已有手势兼容 shim 逐个对比保持不变。

| APK | 字节 | SHA-256 |
|---|---:|---|
| XREAL-StableScreen-v0.3.1.apk | 130849912 | 16a9bf21cce62bd39b1caf495e44118e5ca52834bf1211fb7f5196c0d3d80038 |
| XREAL-RuntimeAudit-v1.0.4.apk | 131117878 | e48c05a8048489b32a1c184a8cd33974a4213ffce665e27c775480438b9ead47 |

机器可读结果：`evidence/stable-screen-apk-verification.json`、`evidence/runtime-audit-apk-verification.json`。本次没有设备安装、无线调试或现场故障复现。

参考：[Unity XR Hands 更新阶段](https://docs.unity3d.com/Packages/com.unity.xr.hands@1.3/manual/hand-data/xr-hand-access-data.html)、[Unity FrameTimingManager 支持范围](https://docs.unity3d.com/2022.3/Documentation/Manual/frame-timing-manager.html)、[Android Thermal API](https://developer.android.com/games/optimize/adpf/thermal)。
