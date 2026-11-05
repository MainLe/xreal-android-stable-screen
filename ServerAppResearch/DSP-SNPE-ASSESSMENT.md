# 小米 15 / Android 17 的 DSP、SNPE 适配调查

日期：2026-11-02。全过程使用现存 APK、SDK 和此前日志，未连接手机。

## 已确认的失败链

此前简述“第三方应用、DSP 不可用”只描述了结果，不足以解释原因。重新检查首次初始化日志，存在更具体的链路：

1. 客户端加载应用包内 `libSNPE.so`、`libSnpeHtpV79Stub.so`。`hands-debug-maps.txt` 可确认两者来自 Demo 的原生库目录，`libcdsprpc.so` 来自设备 vendor。
2. FastRPC 通过系统 HAL 创建 domain 3 的用户进程域，日志明确为 `Unsigned:Y`。这证明该次调用并非在申请 DSP 会话时就被系统完全拒绝。
3. 当前 `ADSP_LIBRARY_PATH` 只有系统 DSP 目录，没有客户端 `applicationInfo.nativeLibraryDir`。
4. 实际成功打开的是 `/odm/lib/rfsa/adsp/./libSnpeHtpV79Skel.so`，不是 Demo 自带同名文件。
5. 版本检查报告 `Skel lib id mismatch: expected (v2.36.0.250627101419_123260), detected ()`。其后有 `QNN_COMMON_ERROR_INCOMPATIBLE_BINARIES`，错误码 2008。
6. `checkHexagonDSP`、`checkHexagonUnsignedPDDSP` 失败，继而选择 `graph_flora_cpu.txt`；CPU 图另外缺少两条输入连接。

证据位置：`evidence/hands-current.log` 第 4316、4486、4503、4509、4536、4556、4646、4650 行。原始日志未改写。

`detected ()` 表示没取得有效构建标识；不能据此认定系统那份库一定是某个旧版本。日志也有 `QcSoCServiceUtils.dll` 搜索失败与部分 vendor 路径的 SELinux 拒绝，尚未证明它们是否直接造成版本标识读取失败。不能只删除版本检查或强行让能力查询返回 true。

## 可以直接尝试的方案

现有 SDK `nr_common.aar` 和此前 Demo 都已包含配套文件：

- ARM64 主机端 `libSNPE.so`、`libSnpeHtpV79Stub.so`、`libSnpeHtpPrepare.so`。
- DSP 端 `libSnpeHtpV79Skel.so`。

这些文件包含同一构建标识 `v2.36.0.250627101419_123260`；APK 内文件与官方 SDK AAR 对应项逐字节一致。Skel 为 Hexagon ELF，不能当作 ARM64 库用 `System.loadLibrary` 加载；它应由 DSP 加载器查找。

候选改动为在进程内、官方初始化前把应用的原生库目录放到 `ADSP_LIBRARY_PATH` 第一位，保留原有系统路径。未设置原有变量时提供官方原来的默认目录。每次启用手部前再校验一次。官方库调用 `setenv` 时第三参数为 0，即不覆盖已有变量，离线反汇编已验证此处兼容预设路径。

这与高通通过 `ADSP_LIBRARY_PATH` 查找 Skel 的设计一致，参考 [Qualcomm HTP backend / Library Loading](https://docs.qualcomm.com/bundle/publicresource/topics/80-63442-10/htp_backend.html)。候选包不修改官方 SNPE、Stub、Skel 或模型，也不改系统权限、SELinux、系统 vendor 文件。官方运行时自行决定 DSP 是否可用，并保留 CPU 回退及此前的 CPU 图修正。

手部库实际在 Unity 客户端进程中执行，所以这次路径设置放在 Demo 内；只更新 Server 无法保证其他第三方 SDK 客户端自动获益。该做法能否作为 SDK 客户端的通用初始化组件，需要实机验证后再判断。

## 候选包与验证边界

构建入口：`XREALFeatureDemoBuild.BuildDspProbe`。

输出：`Build/Android/XREAL-DspProbe-v1.0.3.apk`，同包名，versionCode 13。候选包早期初始化通过播放器专用宏启用，其他构建入口默认关闭 DSP 路径兼容。Server 无需更新。

离线核验：

- `audit_dsp_runtime.py` 审计 SDK/APK 库一致性、构建标识、历史日志和官方 `setenv(overwrite=0)` 指令；输出 `evidence/dsp-runtime-audit.json`。
- `verify_dsp_probe.py` 核验 APK 签名、版本、压缩包完整性、对齐、实际播放器初始化方法、配套原生库及 SDK 设置恢复；输出 `evidence/dsp-probe-apk-verification.json`。

必须分别验证：目录设置生效、原生 DSP 能力检查通过、DSP 手部计算图和模型成功初始化、真实手部关节持续有效。任一阶段的成功都不能代替下一阶段。未连接手机，因此这些实机项目当前均待验证。

## 若路径修正仍然失败

1. 若报告显示包内 Skel 不存在，检查 Android 安装后的库提取行为；必要时从自身 APK 提取经校验的 DSP 文件到应用可读目录，再设置对应搜索路径。
2. 若仍加载系统 Skel，检查 FastRPC 路径缓存及设置时间；先验证首次冷启动的实际打开路径，不能只反复开启手部。
3. 若已加载包内配套 Skel 仍返回空构建标识，核对其 DSP 端依赖以及 `QcSoCServiceUtils.dll` 等搜索日志。新旧构建标识仍需分别实测。
4. 若配套 2.36 运行时确实与当前设备驱动不兼容，再使用高通正式发布的、支持该设备的完整运行时集合做隔离试验。需一起验证 C API、DLC 模型、DSP 自定义算子与缓存兼容性，不能只替换一个 `.so`。现阶段没有证据必须升级运行时。
5. 如最后确认是系统策略限制应用自带 DSP 文件，普通应用内的设置无法修复该策略；届时根据错误证据决定是否需要 OEM 支持。当前日志尚未到这一结论。
