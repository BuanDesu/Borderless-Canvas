# 显示器与坐标边界（T02）

2026-10-19 反馈改进：当前操作以[自动导航、多屏与工作区说明](interaction-refresh.md)为准。本文保留早期阶段的实现和验证证据；其中单屏限制、手动模式、独立标签页及默认关闭自动接入的描述属于旧版。

状态：2026-10-17，实现只读显示器诊断、物理坐标映射和边界检查。真实窗口移动仍在 T04–T06 之后。

## 坐标契约

| 坐标 | 单位与含义 | 转换 |
| --- | --- | --- |
| 世界 W | 有限 double；窗口在画布上的固定位置 | 不受显示器原点限制 |
| 相机 C | 视口左上角在世界中的位置 | 原点为 `(0,0)` |
| 视口 V | 相机内的局部物理像素单位 | `V = W - C` |
| 桌面物理 P | Windows 虚拟屏幕坐标，允许负数 | `P = V + WorkArea.Position` |

一画布单位对应一物理像素。工作区来自 Windows；左侧或顶部有任务栏时，不能把物理原点默认为 `(0,0)`。模型视口尺寸取主屏工作区大小，刷新尺寸不会改变世界位置、当前相机或导航历史。当前四个示例仍是模型演示，真实窗口地图由 T03 接入。

`DesktopCoordinateMapper` 保存相机和工作区，可双向映射点或把物理矩形反投影到世界。`PixelRect` 使用左/上包含、右/下不包含的整数边界；尺寸必须为正并在 Int32 内。`TryProjectWindow` 对位置和尺寸分别取整，中点远离零；整数尺寸不会因分数相机移动而抖动。位置、尺寸、右/下边界或中间计算越界时返回 false，不截断、不饱和到屏幕边缘、不产生可用于移动的输出。

合法离屏矩形不会自动钳制回工作区。能转成整数不表示可以安全停放到该位置；副屏泄漏、离屏刷新与恢复属于 T04/T05 的验证范围。double 世界坐标仍有精度限制，不是数学上的无限精度。

## Windows 读取

`DesktopDisplayCatalog` 实现 `IDesktopDisplayCatalog`，要求 STA 同步调用。枚举回调只收集 HMONITOR；随后读取设备名、主屏标记、屏幕边界和工作区。几何值必须有效、工作区在屏幕内、设备名唯一且恰有一个主屏。结束前再次读取句柄集合和几何，发现变化则拒绝本次快照；这只能发现观察到的变化，不是操作系统级原子事务。读取失败时界面保留上次结果并提示刷新。

显示器和窗口矩形读取都进入短暂的 PerMonitorV2 线程 DPI 上下文，并在 finally/Dispose 恢复调用方上下文；作用域内不 await、不运行消息循环。这样即使调用方暂时处于 DPI unaware 模式，也不会把虚拟化坐标混入物理模型。

有效 DPI 使用 `GetDpiForWindow`：在目标屏幕上创建本进程的 1×1 STATIC popup，设置 TOOLWINDOW/NOACTIVATE，不设置 VISIBLE，不显示也不激活；确认所属显示器后读 DPI，并立即销毁。创建失败、归属不符或 DPI 为零时记为未知，不假定为 96。选择此方式是因为微软不建议在 per-monitor-aware 线程上使用 `GetDpiForMonitor`。DPI 仅用于诊断与基线判定，不乘除世界坐标或缩放应用内容。

`DisplayLayout.IsSingleScreen100Percent` 仅在一个显示器且有效 DPI 为 96 时为 true。即使满足这个条件，也只是配置符合初始基线，不是授权接管窗口或真实平移已验收。多屏、非 100% 和未知 DPI 在界面明确标为超出基线。

## 验证证据与边界

2026-10-17，Windows 11 build 26200、.NET SDK 10.0.401，Release 构建 0 警告、0 错误：

- 总计 48/48 项模型检查通过，其中新增 12 项覆盖负坐标工作区、四边任务栏、点/矩形往返、保持原生尺寸、取整、Int32 两端、溢出中间值、无效拓扑、快照不可变和视口尺寸更新。
- 总计 12/12 项原生检查通过，其中新增 6 项覆盖 PMv2 屏幕几何交叉检查、实际窗口 DPI 比对、unaware 调用者的显示器/窗口物理坐标与上下文恢复、连续八次探针无 HWND 泄漏及焦点变化、MTA 拒绝。
- 实际机器为单屏 2560×1440、144 DPI（150%），工作区 `(0,0)–(2560,1368)`，底部保留 72 个物理像素。界面正确提示超出 100% 基线。
- 已查看 `artifacts/display-smoke.png`，修正说明行和列宽截断后复查。模型页、窗口页和显示器页均完成透明渲染检查。
- 负坐标显示器、顶部/侧边任务栏、100% DPI 的规则由构造数据验证；本次没有对应硬件实测。没有改变用户缩放、分辨率、任务栏设置或用户应用窗口。

`artifacts/native-displays.json` 保存本地显示器证据，不含窗口标题；`artifacts/native-discovery-checks.json` 保存原生检查结果。artifacts 不提交到仓库。窗口检查页截图可能有窗口标题，同样仅本地保留。

当前不自动监听显示配置变化，不支持多屏接管，也未验证应用跨 DPI 时响应 WM_DPICHANGED 的尺寸行为。热插拔、自动隐藏任务栏、不同 DPI 应用、休眠与 Explorer 重启仍须 T11 的真实兼容矩阵验收。

## 官方依据

- [EnumDisplayMonitors：虚拟屏幕枚举](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors)
- [MONITORINFO：屏幕/工作区允许负坐标](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo)
- [SetThreadDpiAwarenessContext：返回旧线程上下文](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setthreaddpiawarenesscontext)
- [GetDpiForWindow：按窗口 DPI awareness 返回有效 DPI](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)
- [GetDpiForMonitor：per-monitor-aware 线程调用限制](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor)
- [CreateWindowExW：创建系统类与隐藏 popup](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createwindowexw)
