# DSP / SNPE 路径兼容候选包

日期：2026-11-02。包名 `dev.xreal.server.features`，版本 `1.0.3-dsp-path`，versionCode 13。

文件：`XREAL-DspProbe-v1.0.3.apk`。手动传输并覆盖安装 Demo 即可，Server 无需更新。此包尚未进行手机实测。

## 本次改动

原来的日志显示：应用携带 SNPE 2.36 配套库，但 DSP 搜索路径没有应用原生库目录，实际打开系统目录里的 V79 Skel，随后版本验证失败。新包在早期初始化和启用手部前，把应用原生库目录放在搜索路径首位，让 DSP 有机会加载包内配套文件。

官方运行时库与模型保持原样，继续由官方能力检查决定是否采用 DSP；CPU 回退和 CPU 图修正仍然保留。没有强制伪造 DSP 支持，也没有修改手机系统。

## 手动测试

1. 安装后彻底退出旧 Demo 进程，再从 Server 启动“手部骨架 / 手势交互”。需要全新进程，避免原来的 DSP 路径缓存继续生效。
2. 在环境光适当的地方把手放在眼镜视野内，观察是否显示骨架、能否捏合交互、保持一分钟是否稳定。
3. 点击“报告 → 保存并复制诊断报告”。新增 `handDspEnvironment` 记录设置前后路径和库文件可见性。

字段含义：

- `pathConfigured`：设置并读回路径成功。
- `pathStillEffective`：保存报告时路径仍与设置值相同。
- `v79SkelPresent`：应用原生库目录中能看到包内 V79 Skel。
- `dspInference` / 界面 `DSP use unverified`：这份报告无法独立证明算法使用 DSP；该文字不是 DSP 初始化报错。
- `handCompatibility` 的 `graph copies repaired`：若增加，说明至少发生了 CPU 图修正；手势能工作时仍可能是 CPU 回退。

上述路径字段为 true 不等于手势成功。DSP 启用需要原生日志确认运行时能力检查通过、选用了 DSP 手部图，并且实际关节有效。用户可先反馈复制的 JSON 与眼镜表现；需要进一步信息时再安排设备日志，当前构建和离线分析不要求连接手机。

详细证据与后续分支见 `D:\Xreal\ServerAppResearch\DSP-SNPE-ASSESSMENT.md`。
