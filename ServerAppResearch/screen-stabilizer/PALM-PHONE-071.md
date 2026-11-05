# 0.7.1 掌面持屏与正面显示

2026-11-04 用户纠正位置：屏幕应平行于掌面、位于掌心侧，类似手持手机；随后明确只有掌心朝向眼睛时才显示。
“与四指方向垂直”本次按屏幕横边垂直于手腕到四指根部的纵向轴处理，屏幕顶部沿该纵向轴。

## 0.7.0 偏差

旧版用世界 Vector3.up 偏移 12cm，并把首次头部朝向校准进 Palm 相对旋转，形成悬在手上方的面板。
SDK Palm 旋转的轴向也没有明确映射到掌面，因此“跟随旋转”本身不能保证面板平行于实际手掌。

## 解剖坐标

新增 PalmSurfaceFrame，以 Wrist、IndexProximal、LittleProximal 和 MiddleProximal 的位置构建坐标，
Middle 缺失时用 Index/Little 根部中点；中心使用有效 Palm 位置，缺失时用腕与指根中间位置。
不使用指尖方向，因此四指弯曲或捏合不会被当成掌面朝向变化；不使用 SDK Palm 旋转。

- 纵轴 fingers = middleRoot - wrist；横向参考 across = indexRoot - littleRoot。
- 左手掌心法线 normal = normalize(cross(fingers, across))，右手使用相反符号。
- 屏幕使用 -Z 为可见正面，rotation = LookRotation(-normal, fingers)。
- 屏幕中心 = palmCentre + normal * 0.03m；屏幕平面与掌面平行。

缺少腕/食指根/小指根、非有限坐标或几何退化时，不使用不可靠的备用朝向，改为隐藏并暂停触控。
绑定跟随完整掌面位姿；取消了此前“始终面向头部”的分支，避免与平行掌面要求冲突。

## 可见性与触控

FacingDot = dot(palmNormal, normalize(headPosition - screenPosition))。
尚未显示时需大于 cos(80°) 才显示，已显示时低于或等于 cos(85°) 隐藏，两条阈值均在掌心正面。
带宽只防止侧面边界反复显隐，不允许背面显示。判定使用头部位置到屏幕中心的方向。
首次尺寸适配推迟到首次面向眼睛，避免手背朝向时按远处位置决定近手屏幕大小。

StabilizedScreen.RefreshPresentation 同时控制手机图像、边框和提示 HUD；掌面不可见时均隐藏。
左手 LateUpdate 之后立即刷新呈现状态，HandPhoneControl 随后根据可见性禁止输入并隐藏指示与导航。
可见性变化递增输入几何版本，结束当前接触，恢复显示后要求重新张开/退回前方准备。
Java 同时以 handScreenVisible 保护 phoneInputAllowed，并在状态变化时立即收到报告。
诊断新增 leftHandScreenVisible、handScreenFacingDot、固定掌面放置说明。
手势骨骼显示独立于面板显隐；跟踪丢失时内部保留上一位姿，但不继续显示旧画面。

## 验证范围

Unity 回归检查掌心符号、左右手镜像、任意世界旋转下坐标不变、退化拒绝、3cm 掌心侧偏移、
平面平行和横边方向、平移转动跟随、头部移动不改变放置、正面/背面/侧面显隐、阈值带宽、
丢失隐藏及恢复、首次背面不校准尺寸。食指投影、距离球和既有触控注入检查继续通过。
这些是离线验证；实际佩戴方向与显隐需要用户安装本版后确认。

成品：`Build/Android/XREAL-StableScreen-v0.7.1.apk`，versionCode 13，130,914,028 字节。
SHA-256：`b391cfd4a77bcf0f168d2dcac42e911b5387634b83e99b1ff1a8f5e9e5a8255d`。
构建、签名、ZIP 对齐、GLES 着色器和打包实现核验通过，73 个 SDK/兼容原生库保持一致。
PhoneGestureService.java 和 SdkRefreshDriver.java 与 0.7.0 完全相同。
