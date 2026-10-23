# 恢复日志与独立恢复入口（T04）

2026-10-18：T04 恢复会话已接入 T06 真实画布管理。所选窗口全部登记后才开始平移；暂停和正常退出恢复原位，旧会话仍须显式处理。版本化 JSON、独占文件租约、原生实例标记与独立控制台程序保持同一协议。

## 使用

```powershell
# 仅检查上次会话，不移动窗口
.\scripts\recover.ps1 -Configuration Release -Inspect

# 恢复日志中的已验证旧会话窗口
.\scripts\recover.ps1 -Configuration Release

# 指定日志；可用于诊断与本地测试
.\scripts\recover.ps1 -Configuration Release -JournalPath 'C:\path\recovery.json' -Inspect
```

默认路径为 `%LOCALAPPDATA%/BorderlessFlatWindow/recovery.json`。脚本优先使用已构建的独立 EXE；缺失时只构建 Recovery 项目和依赖，不依赖 GUI 编译或启动。也可直接运行 `BorderlessFlatWindow.Recovery.exe [--journal <path>] [--inspect]`。退出码 0 表示处理完成，2 表示仍有失败记录，1 表示参数、文件、租约或其他错误；输出为无窗口标题的 JSON 报告。

GUI 新增“恢复日志”页，显示待处理数量和实际路径。启动发现待恢复记录时定位到该页；“检查”不移动窗口，“启动独立恢复程序”异步运行同一控制台程序并显示结果。日志被正在运行的管理会话占用时，恢复程序拒绝进入，不与管理器同时写入。

## 接管登记顺序

1. 重新核验窗口身份和准入状态，读取原始 WINDOWPLACEMENT、显示状态和物理矩形；不保存标题或文档内容。
2. 把身份、原始状态、随机实例标记及 `Armed=false` 写入日志。临时文件与目标位于同目录，WriteThrough + Flush(true) 后原子替换；写入成功后才改变内存提交状态。
3. 在目标窗口的 property list 上设置会话 GUID 名称和随机非零整数值。属性数据是整数标记，不指向管理器进程内存。再次核验 PID、进程启动时间和标记。
4. 原子保存 `Armed=true`，此后 `CanMove` 还须在动作前再次核验当前身份和标记。任一步失败都不授权移动；未完成的记录恢复时仅清理标记，不改 placement。

同一会话重复登记保留第一次原始状态。新进程读到旧的未完成日志时，不能开始新接管；构造/检查会话和 Dispose 也不会悄悄恢复旧记录。只有显式 RecoverAll 才处理旧会话。

文件租约由打开 `.lock` 文件的独占 OS 句柄持有，不以磁盘上有没有 lock 文件判断进程存活。正常退出/崩溃释放句柄，留下的空 lock 文件不会形成假死锁。失败、超大、未来版本或缺字段的日志被保留并拒绝使用，不覆盖成空布局。单文件上限 8MiB、最多 10000 条恢复记录是日志防损坏限制，与动态标签数量无关。

## 恢复与身份边界

原生恢复直接读取当前 HWND、PID、进程启动时间和实例标记；不信任旧目录快照。已关闭、进程变化或标记不符的窗口报告跳过，不调用 placement API。标记覆盖“同进程 HWND 在管理器崩溃后被复用”的情况：新窗口不会自动继承旧窗口的属性。进程身份暂时无法查询时保留失败记录供重试。

恢复使用保存的 WINDOWPLACEMENT 原生工作区坐标，不把 rcNormalPosition 当作物理屏幕坐标传给 SetWindowPos。读取和恢复均使用短 PMv2 上下文。普通/最小化状态采用不激活的 show command；原始最大化状态保留其原生 show command，可能影响前台状态。即使当前看似已在原位，也发送恢复请求：旧异步移动可能尚未被目标线程执行，不能提前释放标记。

跨输入队列的恢复使用异步 placement 请求，再以有界的 WM_NULL 响应检查确认目标线程可用；150ms 无响应即保留日志供重试。随后在 1200ms 内重新读取确认原始 normal rectangle 与 show state；超时或失败保留记录。每完成一个条目就落盘进度；部分失败允许重试。恢复一开始即禁止本会话继续移动，恢复完成后才解除阻断。正常新会话 Dispose 自动尝试恢复并释放文件租约，失败时留下日志与 LastCloseError。

实例标记是防止意外复用的校验，不是防恶意同权限进程的安全边界。Win32 没有把跨进程身份检查和窗口操作合成原子事务的 API；动作前后重复检查降低竞态，T06 管理会话在关闭、状态变化或移动失败时暂停恢复全组。关闭窗口无法再被恢复；标记被外部删除时也不会仅凭相同三元组强行操作。

当前测试覆盖同一显示配置。显示器/DPI 变化或应用自行约束尺寸导致 Windows 调整原始 placement 时，严格确认可能失败；保留记录，不声称已按原位置恢复。多屏拔插、最大化/全屏应用和裁剪/样式恢复仍须 T05/T11 验证。当前流程只改变位置/显示状态与本程序标记，尚未修改其他样式或窗口区域。原子文件替换与 flush 不构成对任意存储故障或掉电的绝对保证。

## 验证证据

`scripts/check.ps1 -Configuration Release -SmokeUi -NativeChecks`：构建 0 警告、0 错误；73/73 模型检查、6/6 文件检查、26/26 原生检查通过。

- 新增 12 项模型检查：写前日志、第一次/armed 写入失败、标记失败、重复登记、旧会话阻断、unarmed 清理、正常退出、部分失败/重试、进度落盘失败、所有权失效和无效日志。部分恢复失败后 CanMove 必须为 false。
- 6 项真实文件检查进入默认 CI，不需要交互桌面：负坐标往返、独占租约、损坏/缺字段/未知版本保留、目标文件锁定时替换失败保留旧字节并清理临时文件、文件大小限制、多次完整 revision 与无标题存储。
- 新增 7 项原生检查：正常退出恢复、最小化后恢复原普通状态、已关闭窗口、缺失实例标记、PID/创建时间变化、unarmed 日志，以及跨进程强制结束演练。
- 强制结束演练由测试进程持有透明窗口，子管理进程登记并持有租约；父进程只移动自己的测试窗口，再强制结束这个已知子进程。独立 Recovery EXE 的 inspect 确认记录保留且不移动窗口，实际恢复确认原矩形、状态和日志清空。管理进程还活着时，独立程序因租约拒绝进入。
- GUI 使用本地构造日志渲染待恢复分支，已查看 `artifacts/recovery-smoke.png`，按钮、数量和说明显示完整；没有通过该截图宣称真实按钮操作已做人工验收。真实进程启动/恢复链路由上述跨进程检查覆盖。

所有原生测试仅操作本项目创建的透明测试窗口；测试 journal 位于 ignored artifacts，GUI 构造日志位于 ignored .local。没有接管或恢复用户当前应用。测试结果合并在 `artifacts/native-discovery-checks.json`，真实截图可能含标题，仅本地保留。

2026-10-18 集成复验：真实 GUI 分三组接入五扇自有跨进程窗口，暂停和继续后，核验进程路径及日志身份再强制结束管理器。独立 inspect 为五项 armed，重启 GUI 自动提示恢复；独立恢复 EXE 确认五项原始 placement/状态，剩余零项，GUI 刷新为零，原生测试文字保留。详见[FR-02 记录](runtime-window-admission.md)。损坏日志另有独立 EXE 文件检查，返回受控错误码 1 且不改写原文件；当前恢复文件检查为 7 项。该验证补齐真实管理器故障流程，不代替所有第三方应用与硬件验收。

## 官方依据

原位置完全离开当前显示器时的确定性恢复、物理/workspace 坐标与拒绝移动后的重试见[显示器缺失恢复](recovery-display-fallback.md)。部分可见位置仍精确恢复；原始日志不改写。

- [WINDOWPLACEMENT 与工作区坐标](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement)
- [GetWindowPlacement](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowplacement)
- [SetWindowPlacement](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowplacement)
- [SetProp 与 UIPI 限制](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setpropw)
- [Window Properties](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-properties)
