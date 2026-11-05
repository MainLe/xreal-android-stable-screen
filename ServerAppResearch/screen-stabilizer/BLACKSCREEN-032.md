# 0.3.1 手机画面黑屏回归与 0.3.2 修复

本次只处理眼镜中手机画面不显示，不改动姿态滤波、跟踪或防抖策略。

2026-11-03 无线实测：已安装版本 0.3.1 / code 5；手机进程正常，Air 2 Ultra 为 3840×1080 / 60Hz。MediaProjection 明确采集 display 0 到虚拟 display 24，系统授权有效。采集、Unity 上传和渲染计数持续增长。重新居中后，眼镜只显示面板背景，手机内容为黑色；同一会话的虚拟显示截图则包含完整手机控制页。由此将问题缩小到应用图像渲染路径，而非没有授权或没有采到手机画面。

证据：

- `evidence/stable-screen-v031-source.png`：系统投屏源，包含完整手机页面。
- `evidence/stable-screen-v031-centered.png`：居中后的双眼输出，只有背景面板。
- `evidence/stable-screen-v031-missing-image.log`：本应用进程运行日志。
- `evidence/unity-stable-screen-build-v031.log`：旧版实际 GLES 编译错误。

直接根因：0.3.1 将网格调试分支加入手机纹理的同一 shader，其中局部变量 `line` 被 GLES 着色器编译器当成保留标记，报 `syntax error: unexpected token 'line'`，同时引发后续 lerp 解析错误。旧构建日志显示该手机 shader 的 GLES internal programs 为 0。Unity 的 BuildPlayer 最终仍返回成功并生成 APK；旧核验只检查脚本 / Gradle / 签名等，漏掉 shader 编译失败，因此错误版本被交付。

0.3.2 修改：

1. 手机投屏 shader 恢复为单纯的纹理采样路径，移除网格条件分支。
2. 网格使用单独的 `CalibrationGrid.shader` 和 `ReferenceMaterial`。变量改为 `gridLine`、`crossMask`。
3. 切回手机画面时显式恢复手机材质，并通过 `_MainTex` 绑定实际帧纹理；网格开启期间忽略尚未停止的采集尾帧，防止尾帧抢回显示模式。
4. 增加低频 `[StableScreenImage]` 记录：CPU 纹理 RGB 非黑样本数、shader 名、纹理绑定和当前渲染材质，用于确认实际图像上传。
5. 构建监听 shader 编译错误，并检查两份 shader 的编译消息。离线核验也拒绝任何 `Shader error in`，不再把 APK 文件生成视作 shader 成功。

没有升级 Server 或修改原生 SDK .so。本次不评价、修复或继续测试抖动稳定性。

最终验证：0.3.2 的手机和网格 GLES shader 各包含 6 个有效 internal programs，编译没有错误。APK SHA-256 为 `804d4eae5e331ba534c4bbf4478ed8f240898677cb922d2fc033e295ea89d10b`，字节数 130850580；手机上安装文件的 sha256sum 与交付文件一致。

已在小米 15 / Android 17 / Air 2 Ultra 实测：真实手机页面在双眼显示；网格独立显示并停止 MediaProjection；用户再次同意系统共享后，同一 Unity 进程恢复手机页面。原生位置 / 旋转有效，纹理诊断显示 RGB 非黑样本 64/64、纹理和手机材质绑定正确。用户的 180% 缩放设置保持原样，可能使页面边缘超出当前视野；该缩放不属于本次黑屏问题。

设备记录：`evidence/stable-screen-v032-device-validation.json`。实际输出：`stable-screen-v032-centered-phone.png`、`stable-screen-v032-grid.png`、`stable-screen-v032-grid-to-phone.png`。本次通过的是黑屏修复及显示模式切换，不代表长期运行、抖动、高刷或旋转问题已重新验收。
