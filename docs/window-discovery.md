# T01：窗口身份与准入

本批提供可审阅的只读发现层，不移动或激活真实应用，不安装输入钩子，不切换 Windows 虚拟桌面。

## 读取与判断

Windows 适配器先用 EnumWindows 收集顶层 HWND，再在回调外读取标题、类名、样式、物理矩形、owner、最小化状态、DWM cloak 和当前虚拟桌面状态。查询进程只使用 PROCESS_QUERY_LIMITED_INFORMATION，进程句柄与 COM 对象在读取完成后释放。调用须在 STA 线程执行。

每个窗口的原生身份由 `(HWND, PID, GetProcessTimes 返回的创建 FILETIME)` 组成。读取前后重新检查 PID/进程创建时间及窗口存活；检测到变化则标记关闭/身份改变。不允许用 PID 或 HWND 单独作为模型身份。

Application 层的 WindowAdmissionPolicy 独立于 Windows，可通过合成观察记录验证。优先排除自身、桌面/任务栏/系统浮层、不可见、子窗口、工具窗口、owned 窗口、无标题项，然后检查元数据、进程身份、cloak、虚拟桌面、最小化与尺寸。任一必需状态未知即排除，不将 API 失败默认为成功。

owned 窗口保留 owner HWND 及可读元数据，但不作为独立对象纳入；模态/非模态 owned 窗口采用同一保守规则。关联移动和模态焦点管理尚未实现。

## 稳定身份和快照

- 完整枚举成功后才替换身份表；失败或重复 HWND 快照不清空上次状态。
- 同一原生身份保持 Guid；标题、位置、尺寸或暂时排除状态改变不自动更换 Guid。
- HWND 的 PID 或进程创建时间改变时分配新 Guid；快照中消失或无法继续验证身份时清除旧绑定。
- 同进程内关闭并重用 HWND，可通过 Forget 失效入口更换 Guid；观察到一次缺席也能清除旧绑定。
- 所有 Guid 都是进程会话内身份，当前没有跨重启持久化承诺。

**已知边界：** 手动快照不是原子事务。若同一个进程在两次快照之间关闭并重用同一个 HWND，且关闭过程完全未被观察，仅凭三元组无法区分；T03 必须把 destroy 事件接入失效入口。任何未来移动/恢复仍须在动作前验证身份，不能凭一次旧快照操作。

当前 DesktopWindowCatalog 只做手动检查；不能把它当作已经完成的事件监听器或真实桌面空间地图。

## 界面

“Windows 窗口候选（只读）”默认仅显示可纳入项。勾选“显示被排除的窗口”查看完整结果和原因。显示标题、状态、PID、owner、模型 ID 和窗口类；刷新失败保留上次列表。Home/Backspace 的示例相机快捷键只在模型预览页工作，不抢窗口列表的按键。

## 验证

```powershell
.\scripts\check.ps1 -Configuration Release -SmokeUi -NativeChecks
```

- 36 项模型/策略检查：包括原有 7 项画布检查，以及各准入边界、身份复用、快照失败、owner、最小化恢复和关闭竞态记录。
- 6 项原生集成检查：透明、无激活的自建测试窗口，验证普通窗口准入、自身排除、tool style、owner、改名/移动、隐藏恢复和关闭清理。
- 本机原生检查使用真实 GetProcessTimes、DWM 和公开虚拟桌面查询；不会更改其他应用或切换用户虚拟桌面。
- 正常权限环境的只读桌面快照扫描 308 个顶层窗口，2 个可纳入、3 个因真实 DWM cloak 排除，其余主要为隐藏、工具、系统及自身窗口。数量随桌面状态变化。沙箱拒绝查询其他进程创建时间时，结果会标为身份无法验证；不应因此建议整个产品默认提权。
- cloak、其他虚拟桌面和权限失败的负路径通过合成观察验证；本轮不声称已切换桌面或用受保护进程逐一实测。
- UI 冒烟检查输出示例地图及检查页 PNG。检查页可能含当前窗口标题，位于已忽略的 artifacts 目录，不应上传；JSON 结果只保存计数和测试元数据。
- CI 默认运行无桌面依赖的模型/策略检查，并编译原生检查项目；需要交互桌面的 NativeChecks 在本地显式运行。

## 官方 API

- [GetProcessTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes)
- [GetWindow / GW_OWNER](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindow)
- [DWMWA_CLOAKED](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [IsWindowOnCurrentVirtualDesktop](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ivirtualdesktopmanager-iswindowoncurrentvirtualdesktop)
