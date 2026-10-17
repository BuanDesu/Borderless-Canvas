# Windows 无限画布窗口管理：可行性与设计

核查日期：2026-10-17。核查方式：GitHub 插件检索与源码阅读、微软官方 API 文档交叉验证。本次没有安装运行第三方程序，没有完成本机兼容性或性能实测。

## 结论

可以开发有实用价值的 Windows 无限画布窗口管理软件。二维自由布局、摄像机平移、以光标为中心缩放总览，以及让背景和任务栏保持屏幕固定，都有可实现的技术路径。

需要区分“缩放显示窗口内容”和“在任意缩放比例下直接操作所有原生窗口”。前者已有 Windows 项目；后者不能仅靠调整窗口大小完成，也不能由现有公开预览 API 自动解决。本次资料不足以证明能够对所有软件无缝兼容。

建议首版：原生比例下自由摆放、平移和操作多个真实窗口；缩放时显示实时预览，选择窗口后平滑回到原生比例并交给真实窗口接收输入。这保留空间位置记忆和多窗口并排工作的主要价值，但属于对完整设想的产品折中。

## 逐项判断

| 需求 | 判断 | 实现要点 |
| --- | --- | --- |
| 窗口在大画布任意摆放 | 可行 | 单独保存逻辑坐标；屏幕外窗口不必真的放到极远的系统坐标 |
| 摄像机上下左右平移 | 可行 | 原生操作模式批量移动受管理窗口；预览模式只更新投影 |
| 滚轮、Shift 配合导航 | 可行 | 画布导航模式内滚轮纵移、Shift+滚轮横移；普通操作时保留应用自己的滚动 |
| 同时按鼠标左右键拖动 | 可行，有交互成本 | 需要识别双键组合并处理首个按键、右键菜单及拖选冲突；提供其他可配置手势 |
| Ctrl+滚轮以光标为中心缩放 | 显示可行 | 自己合成实时窗口预览；应用操作模式下需处理浏览器等的同名快捷键冲突 |
| 背景与任务栏固定 | 可行的设计要求 | 不纳入摄像机变换；保留真实任务栏区域及输入，不以任务栏预览代替真实交互 |
| 任意缩放时直接编辑所有窗口 | 尚不能保证 | 要解决输入坐标、焦点、弹窗、输入法、拖放、权限和图形程序兼容性 |

Windows 的 [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos) 能改变顶层窗口位置和尺寸，[窗口定位文档](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)说明可以用 BeginDeferWindowPos / DeferWindowPos / EndDeferWindowPos 批量定位。这支持平移实现；改变窗口尺寸本身会触发布局变化，不能等同于对整张窗口图像做相机缩放。

## 查到的 Windows 项目

1. [CanvasWindowComposer](https://github.com/oreyg/CanvasWindowComposer)：与需求最接近，提供二维平移、光标中心缩放、总览和小地图。README 明确总览由实时 DWM 缩略图组成，选择窗口后返回实际窗口。仓库声明处于早期开发，且当前已归档，适合研究和原型参考。
2. [WinCanvas](https://github.com/sermtech/wincanvas)：实时 DWM 窗口预览组成可平移、缩放画布；F1 聚焦到原生大小，并通过透明区域操作真实窗口。定位偏窗口总览与切换，不能据此认为已经实现任意比例下直接编辑所有窗口。
3. [Spatial Canvas](https://github.com/13auth/spatial-canvas)：用 Windows.Graphics.Capture、D3D11 和 park & swap 实现画布与真实窗口切换。README 明确画布模式隐藏任务栏，因此现状不满足任务栏始终固定可见的要求。其许可证为 proprietary/source-available，不能直接当作可自由复用的开源底座。

对 CanvasWindowComposer 的源码核查固定在提交 `c89634a43fe659482f9e6c7c97f5ed89b94fd701`：

- [Win32WindowApi.cs](https://github.com/oreyg/CanvasWindowComposer/blob/c89634a43fe659482f9e6c7c97f5ed89b94fd701/Source/System/Win32WindowApi.cs#L20)：将 Progman、WorkerW、主副任务栏类排除出普通窗口管理；另有批量定位调用。
- [OverviewCamera.cs](https://github.com/oreyg/CanvasWindowComposer/blob/c89634a43fe659482f9e6c7c97f5ed89b94fd701/Source/Overlays/Overview/OverviewCamera.cs#L54)：通过缩放前的光标世界坐标反算新相机位置，保持光标锚点不动。
- [OverviewThumbnails.cs](https://github.com/oreyg/CanvasWindowComposer/blob/c89634a43fe659482f9e6c7c97f5ed89b94fd701/Source/Overlays/Overview/OverviewThumbnails.cs#L234)：壁纸预览矩形固定；任务栏预览也使用固定矩形。这证明固定视觉层已有实现参考，不能直接证明真实任务栏在所有总览状态下都可正常交互。

## 建议的软件结构

使用独立的逻辑画布保存窗口位置、相机位置和缩放比例。设窗口世界坐标为 P，相机为 C，缩放为 z，视口原点为 O：

`屏幕坐标 = O + (P - C) × z`

光标屏幕坐标为 m 时，先求锚点 `A = C + (m - O) / z`，再令 `C_new = A - (m - O) / z_new`。背景与任务栏不参加这组变换。“无限”指逻辑空间很大，实际只投影可见区域，并按需重置坐标原点。

建议 C++/Win32 + D3D11 做底层窗口与图形处理，或用 C# 承载配置、托盘与管理逻辑并调用原生 API。DWM 缩略图可用于初期总览；需要自定义纹理合成时再使用 Windows.Graphics.Capture。微软文档确认 [DWM 缩略图是动态窗口连接](https://learn.microsoft.com/en-us/windows/win32/dwm/thumbnail-ovw)，[CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow) 从 Windows 10 1903 起可按 HWND 创建捕获项。这些 API 提供画面，不会自动把缩放预览变为可交互的原生窗口。

输入层用明确的“导航状态”解决应用滚动与相机滚动竞争，可配置是否全局接管指定组合键。微软的 [LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc) 支持观察和拦截鼠标事件；回调应快速返回，窗口移动和绘制放到其他处理路径。

固定任务栏的首版应将画布视口限定在显示器工作区，保留真实任务栏；应用激活时把相机带回该窗口。背景使用屏幕固定层；静态壁纸和动态壁纸、桌面图标交互应分别验证。管理器退出或异常时需恢复原窗口位置、显示状态和裁剪区域。

## 必须验证的边界

- 多显示器、混合 DPI、拔插显示器、休眠恢复、Explorer 重启。
- 最大化、最小化、置顶窗口、菜单、模态对话框、文件选择器和输入法候选框；关联弹窗应随所属窗口处理。
- 浏览器、Office、编辑器、终端、Electron、视频应用和常用图形程序；完全离屏或最小化的窗口可能停止输出新画面，不能承诺预览一直刷新。微软的[官方捕获示例](https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/blob/master/cpp/ScreenCaptureforHWND/README.md)明确指出最小化窗口不被捕获。
- 如果尝试缩放状态下的输入转发，[SendInput 受 UIPI 约束](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)，普通进程不能向更高完整性级别应用注入输入。UAC 安全桌面等应作为边界处理。
- 应用可使用 [WDA_EXCLUDEFROMCAPTURE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity) 排除捕获，应允许这类窗口退出画布管理或用占位符表示。
- 需要测量窗口数量增长时的帧率、显存、CPU/GPU 和输入延迟；对不可见窗口停止不必要更新，对预览限帧。当前没有实测数字。

Windows 的[全屏放大 API](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-magsetfullscreentransform)最小倍率为 1.0，不能直接满足缩小查看更大画布的需求。

开发决策：可以进入原型阶段，优先验证“单屏二维平移 + 固定真实任务栏 + 实时缩放总览 + 回到原生窗口操作 + 异常恢复”。任意比例下直接操作所有应用，应作为独立的后续研究项。
