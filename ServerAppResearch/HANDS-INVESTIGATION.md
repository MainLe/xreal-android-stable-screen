# Air 2 Ultra 手部跟踪故障，2026-11-01

设备：小米 15 / API 37。用户反馈其余空间功能正常，手部无骨架。

已通过先前配对的无线调试读取 Demo 报告和原生日志。`SessionTracking`、位置和旋转有效，`HandTracking=True`、XR Hand subsystem running，切换输入返回 True，但双手 `not tracked`。

关键复现（22:20:28）：

- 原生加载的是 Server 中 `libnr_hand_tracking.so`，SHA-256 `925bc761ae0a9d9fbd06a80799e6342c95a48d356cc38ac4a31576df90b74c4f`。
- Ultra 类型为 5。普通第三方应用，DSP / SNPE 不可用，选择 `graph_flora_cpu.txt`（index=4）。
- `ValidatedGraphConfig Initialization failed`：`HandLiftCalculator` 输入缺少 `HEADPOSE`；`HandFilterCalculator` 输入缺少 `GR_KPT2D_INPUT`。
- 手部 Initialize 返回 Status 1000。随后 Unity 收到 `Failure / NativePerception+SetHandTrackingEnabled`。
- 后续 `GetHandData error: 1` 与 hand_num 警告是初始化失败后的表现。静态分析计数判断是 cmp 3 / b.lo，日志中的 2 是最大数常量；没有依据把问题当成“双手数量 2 被错误拒绝”来修改边界。

证据：`evidence/hands-current.log`、`hands-server.log`、`hands-restart.log`。与手势相关的日志应限定 PID 19415（本次客户端）和 16623（本次 Server），避免系统其他 Hand/Handler 日志干扰。

确认：失败在原生 CPU 手部算法图初始化，发生在成功检测手之前；不能归因于 Unity 骨架材质或光照。尚未证明此缺陷普遍由 Android 17 引起，也尚未完成算法图修复。

离线分析工具：`tools/decode_native_strings.py` 使用工程内 Unicorn 模拟已发布运行库的字符串解码函数；`tools/arm64_xrefs.py` 查找 ARM64 字符串地址引用。没有修改手机系统或当前安装的原生库。算法图/模型位于模块的内嵌加密 tar 中，普通日志未输出完整图。

下一步使用 `XREAL-HandsDebug.apk`（同包名、同 Unity 调试签名、versionCode 11、Development）读取失败时图配置并核对端口连接，再做有证据支持的修正。诊断版本订阅 `OnXREALError`，在原生手部初始化失败后明确显示失败，不把输入源被接受当成算法已启动。原正式 APK 和 Server 1.0 保留。当前尚无已验证的手势修复包。

## 后续调查与离线兼容测试版

诊断包已安装。LLDB 全进程附加导致明显暂停，用户反馈界面卡死；会话已结束并解除停止状态。随后改用同 UID 的只读 `/proc/<demo-pid>/mem` 辅助工具，验证可在应用持续运行时读取其原生资源文本。该工具校验目标命令行和 UID，不附加其他进程；不是通过修改 Android 或取得 root 实现。

已恢复 CPU 图全文：3785 字节，SHA-256 `bbb6f0d28a00947040537a1edc0b12c614f3af29e81b6cd7f79773ef053f22e3`。缺失两条边得到实物确认；图中已有其生产者，无需替换模型。用户此时要求断开手机继续离线构建，此后不再调用 ADB。

新增进程内兼容层，在已审计手部模块的图内容复制时提供等长修正版本。双重约束：模块 Build ID `7e71f05b2b0c2d0a1227152906e88b0375da5c8a`；图内容逐字节完整匹配。官方 `.so` 文件和模型保持原样。兼容层不注入其他应用，也不修改系统。新 Demo 单独启用此处理，原构建入口保持禁用。

实际 ARM64 复制处理与导入绑定已通过离线仿真测试，包含真实官方 ELF 和版本不匹配拒绝测试。测试记录见 `evidence/hand-compat-native-test.json`。新增 APK 为 `Build/Android/XREAL-HandFix-v1.0.2.apk`，目前状态是候选修正、待实机验收，不等于手势已经工作。细节见 `../Build/Android/手势修复测试说明.md`。

## 2026-11-02：DSP / SNPE 失败原因的进一步定位

重新检查首次初始化日志发现：普通客户端已经通过 HAL 创建未签名 DSP 用户进程域，并成功打开系统 `/odm` 目录中的 V79 Skel；随后构建标识验证失败，错误为 `QNN_COMMON_ERROR_INCOMPATIBLE_BINARIES`。因此，先前的“第三方应用、DSP 不可用”不能解释为系统仅因应用身份就完全禁止 DSP。

包内实际已有官方 SDK 的 SNPE 2.36、V79 Stub/Skel 配套集合，但原来的 `ADSP_LIBRARY_PATH` 没有客户端原生库目录。新增 opt-in DSP 路径设置候选，保留原生能力检查及 CPU 回退；所有官方原生文件不变。`detected ()` 不能证明系统 Skel 的实际版本，是否只是路径问题仍待实测。完整证据、候选构建和后续分支见 `DSP-SNPE-ASSESSMENT.md`。本次未连接手机。
