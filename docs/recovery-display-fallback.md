# 原显示器不可用时的恢复

2026-10-23 专项复现：恢复日志保存的正常窗口位置完全在当前全部屏幕之外时，Windows 会调整 `SetWindowPlacement` 请求使窗口可见。此前恢复器仍要求读回原始坐标，等待 1200ms 后留下待恢复记录。测试使用自己的透明窗口和合成历史坐标，没有拔插或修改真实显示器。

微软明确说明 [SetWindowPlacement 会调整完全离屏的位置](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowplacement)。修复采用确定的恢复目标，而不是将任何可见结果当成恢复成功。

## 恢复规则

1. 仍先核验 HWND、PID、创建时间、durable armed 记录和实例标记。
2. 用已记录的物理矩形与 [MonitorFromRect](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfromrect) 检查当前屏幕。原矩形只要仍与任一屏幕相交，就继续精确恢复原 placement，不因部分越界而改变用户原位置。
3. 完全离屏时，选择最近屏幕的可用工作区，保留原生尺寸，将窗口放入可用范围。窗口大于工作区时保留尺寸，左上角对齐可用区域。只有原记录为正常状态、placement 与物理尺寸一致且整数运算有效时才规划。
4. 根据当前屏幕与工作区的偏移转换为原生 workspace 坐标，再调用 `SetWindowPlacement`。不把原始 workspace 坐标传给屏幕坐标 API，见 [WINDOWPLACEMENT 坐标约定](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement)。
5. 目标线程响应后，同时核验 placement、显示状态、实际物理矩形及当前目标显示器/工作区仍与规划一致，才释放标记和恢复记录。窗口拒绝、配置再次变化或读回不一致仍保留原记录，供重试。日志中的原位置不会被回退目标覆盖。

恢复不激活窗口。非正常状态或尺寸关系不明确的离屏历史记录暂不自动猜测，保留记录并说明原因；正常情况下的原有最小化恢复路径保持精确核验。混合 DPI、应用自行调整尺寸、真实热插拔仍须硬件与应用验收，不能以此宣布 AT-12 完成。

## 检查

- 三项模型检查：四边任务栏的坐标转换、负屏幕原点、超大窗口、显示状态/尺寸歧义、无效工作区和整数溢出。
- 自有原生窗口的合成历史完全离屏位置，验证恢复后原生尺寸、可用区域、前台与标记释放。
- 自有窗口拒绝移动时，验证原日志和标记保留；允许移动后再次恢复完成。该例同时防止只看 `GetWindowPlacement` 而忽略实际窗口矩形。
- 部分可见的原位置验证精确恢复，不进入回退。

专项入口：先构建 Release，再运行 `dotnet run --project tests/BorderlessFlatWindow.Windows.Checks --configuration Release --no-build -- --recovery-display-only`。检查结果仅为自有夹具，不是已执行真实设备切换的声明。
