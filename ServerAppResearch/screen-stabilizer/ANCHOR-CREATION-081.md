# 0.8.1 锚点创建生命周期修正

用户确认 0.8.0 已识别真实平面，但无法建立锚点；暂时不能连接调试。
本轮仅离线分析和构建，不声称已经确定设备上的唯一原因或修复全部原生兼容问题。

## 已确认的代码问题

0.8.0 在 AddComponent 后立即检查无效 ID 并销毁组件，失去 ARAnchor 后续注册机会；
同时要求 !pending && validId && Tracking 才显示。AR Foundation 5.1.5 的 pending 表示
手动创建后仍在等待子系统 added/updated 通知，与 XRAnchor 自带的 TrackingState 独立。
CreateTrackableImmediate/CreateTrackableFromExisting 均会把成功创建的对象设为 pending=true。
因此有效原生 Tracking 不能只因该通知标志而被拒绝，工作 Demo 也按 trackingState 判断可见性。

Demo 还显式创建 SpatialMaps 并设置映射目录、使用 TrackablesParent 坐标空间。
新版按相同初始化顺序使用独立 StablePlaneMaps，并等待管理器及真实 6DoF 就绪。

## 注册策略

PlaneAnchorCreation 保留确认的位姿，最长 15 秒、最多 12 次、最短间隔 750ms 请求注册。
使用本工程 AR Foundation 5.1.5 提供的同步 ARAnchorManager.AddAnchor 工厂，
它在 provider 临时返回 false 时返回 null，成功时自动放入 TrackablesParent。
该 API 在此版本仍可用但标为 obsolete；此处有意使用它控制重试节奏，避免未注册 ARAnchor
组件在 Update 中每帧请求原生创建。仅本工程包装调用，无 SDK 或 AR Foundation 包修改。

返回有效原生 ID 后不再重复创建；以 TrackingState.Tracking 和有效 ID 判断可用，pending 仅记录诊断。
Limited/None、无效 ID、取消或过期请求不显示成功。创建期间保留平面检测，真正 Tracking 后才停止扫描。
取消时释放唯一锚点，保留原有 manager 延迟关闭顺序；不降级为普通 Transform 固定并冒充原生锚点成功。

## 可复现的离线检查

PlaneAnchorLifecycleChecks 使用真实 AR Foundation 管理器和 Editor-only 受控 XRAnchor provider，
第一次返回 false、第二次返回 Tracking 的有效锚点。真实管理器返回的对象确实同时 pending=true，
并正确以 TrackablesParent 转换世界位姿。新策略接受该状态，旧 !pending 条件会拒绝。
另外覆盖服务未就绪、限频、已有注册不重复、Limited/None、最大尝试数、15 秒截止及取消后不恢复。
受控 provider 位于 Assets/Editor，成品核验要求它和测试类不进入 APK；这不是手机原生算法测试。

## 诊断

新增 planeAnchorDiagnostics：管理器/服务状态、provider ID、目录是否初始化、尝试次数、
最近错误、ID/pending/tracking、创建经过时间、added/updated/removed 计数及最近变更年龄。
Native 锚点错误回调和创建返回值会进入本应用日志。映射目录重试与原生创建均限频。

这些修正覆盖可确认的生命周期缺陷；真实 SDK 是否因目录、原生拒绝、跟踪不足或其他兼容问题失败，
仍需用户之后的实机结果或诊断确认。

0.8.1 构建与离线核验通过：versionCode 17，130,943,980 字节。
SHA-256：`e2dc0b1b718628a0c4f401bb19201089b2e91b975acf286f955dd08bc3796b63`。
成品签名、ZIP 对齐、GLES 和打包实现核验通过，73 个 SDK/兼容原生库保持一致；
Editor 受控 provider/测试类已确认不在 APK 内。PhoneGestureService 和 SdkRefreshDriver 源哈希与 0.8.0 一致。
