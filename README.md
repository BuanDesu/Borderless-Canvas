# Borderless Flat Window

[GitHub 私有仓库](https://github.com/BuanDesu/Borderless-Flat-Window) · [开发任务](https://github.com/BuanDesu/Borderless-Flat-Window/issues) · [里程碑](https://github.com/BuanDesu/Borderless-Flat-Window/milestones)

Windows 原生的连续二维窗口画布：窗口保留空间位置，屏幕作为可移动视口；通过空间总览定位窗口。应用窗口保持原生比例。

当前已补齐 **FR-02 运行中新窗口接入**：支持手动加入及可选自动接入新窗，优先放在视口附近空位，保留既有布局、相机和标签；五窗主程序强制结束及独立恢复实测通过。191 模型、15 文件、48 原生检查通过。下一步普通应用兼容、任务栏与组合手势验收及试用打包。操作与边界见[运行中接入](docs/runtime-window-admission.md)、[位置标签](docs/location-bookmarks.md)和[原型交付检查](docs/prototype-plan.md)。

## 快速开始

开发环境：Windows、.NET 10 SDK（含 Windows Desktop 引用包）。无需 WSL、Node.js 或第三方 NuGet 包。

在项目根目录的 PowerShell 中运行：

```powershell
# 编译整个解决方案
.\scripts\build.ps1

# 执行模型与恢复/标签文件检查；加 -SmokeUi 可渲染页面及降级状态
.\scripts\check.ps1 -SmokeUi

# 在具备交互桌面的 Windows 上检查透明原生测试窗口
.\scripts\check.ps1 -Configuration Release -SmokeUi -NativeChecks

# 启动桌面画布程序
.\scripts\run.ps1

# 独立检查上次会话的恢复日志；去掉 -Inspect 才会恢复已验证窗口
.\scripts\recover.ps1 -Configuration Release -Inspect

# 运行自己的测试窗口，验证停放、预览、恢复与原生输入
dotnet run --project tests/BorderlessFlatWindow.Probe --configuration Release
```

也可以直接用 SDK：

```powershell
dotnet build BorderlessFlatWindow.slnx
dotnet run --project tests/BorderlessFlatWindow.Checks
dotnet run --project src/BorderlessFlatWindow.App
```

脚本将 CLI 临时状态写入忽略目录 `.local/`，并关闭与本项目无关的工作负载探测。若受限环境中的默认构建遇到 SDK 探测问题，优先使用脚本。`global.json` 允许已安装的 .NET 10 稳定版 SDK feature band。

## 当前可以操作什么

- 在“窗口画布”勾选普通窗口，点击“开始 / 恢复画布”；只接管勾选项。
- 运行中可勾选其他候选并“将勾选新窗口加入画布”；可选“自动接入之后新开的窗口”（默认关闭），不顺带接管原有未选窗口。新窗优先放在当前视口空位，不重排旧布局。
- 用该页的箭头或地图内滚轮平移真实窗口，Shift＋滚轮横移；点击空间卡片定位目标。Home/Backspace 在该页返回原点/上一视角。
- “暂停并恢复原位”与正常关闭恢复接管前位置；同一选择再次开始会保留本次进程的世界布局和相机。启动发现旧日志时先处理恢复。
- 运行画布后用 Ctrl+Alt+Space 切换全局导航，滚轮纵移、Shift 横移、左右双键拖动；导航中 Home/Backspace 返回原点/上一视角，Esc 退出。可在下拉框更换切换组合，冲突会显示；退出导航后再点击或编辑应用。
- “导航设置”分别开关两轴滚轮和双键拖动，并设置每格移动距离。关闭的手势直接交给应用；当前设置仅本次进程有效。
- Ctrl+Alt+O 打开空间总览；输入标题搜索，方向键按空间选择，Tab 轮选重叠项。Enter/单击确认后定位，Esc 取消并保持相机；无匹配或目标关闭时不会误跳转。F5 手动适配全部。
- 在“空间模型预览”中点击矩形，将模型相机定位到该窗口。
- 使用箭头按钮或滚轮移动相机；Shift＋滚轮横移。
- Home 返回原点；Backspace 返回上一视角。预览中的虚线是视口，十字是原点。
- 在“Windows 窗口候选（只读）”中点击“检查窗口”，查看可纳入的窗口与稳定模型 ID。
- 勾选“显示被排除的窗口”查看原因，例如自身、桌面/任务栏、工具/所属窗口、DWM cloak、其他虚拟桌面、最小化或无法验证的状态。
- 窗口创建、关闭、改名和移动通过 WinEvent 按窗口增量更新；重命名和移动保持已验证身份，destroy 撤销旧身份。失败保留上次界面，可手动重查。
- “显示器与坐标（只读）”显示主屏、物理边界、可用工作区和有效 DPI；可手动刷新。模型视口取主屏工作区尺寸，坐标以物理像素为单位。
- 当前单屏 100% DPI 基线已有模型检查；本机 150% DPI 的只读检查通过，多屏与混合 DPI 的真实窗口管理尚未验收。
- 在“真实空间地图（只读）”观察实际位置和大小，金框标记前台窗口；可适配全部窗口。点击/滚轮仅改变地图预览相机，真实窗口保持原位。地图比例在当前视图中固定，窗口移动时不会自动重排。
- 在“恢复日志”检查待处理记录，或启动独立恢复程序。恢复仅操作已有日志且当前身份/实例标记匹配的窗口；损坏日志和失败项目保留，详细命令见[恢复说明](docs/recovery.md)。

“窗口画布”会移动已登记的真实窗口；另两个模型/只读地图页面只改变预览。当前仅开放 DPI 已知的单显示器普通窗口，最大化、全屏和置顶需先退出对应状态；关联弹窗、关闭或移动失败会暂停并恢复。实际 Shift/双键组合、普通应用和任务栏路径继续验收。

## 目录

```text
BorderlessFlatWindow.slnx
src/
  BorderlessFlatWindow.Core/          # 纯几何、相机、总览投影与领域记录
  BorderlessFlatWindow.Application/   # 画布会话、导航历史、位置书签、平台端口
  BorderlessFlatWindow.Windows/       # Win32 发现、事件、受保护移动、恢复和预览
  BorderlessFlatWindow.App/           # WinForms 管理入口、空间地图与诊断
  BorderlessFlatWindow.Recovery/      # 独立控制台恢复入口，不依赖 GUI 启动
tests/
  BorderlessFlatWindow.Checks/        # 无第三方依赖的可执行模型检查
  BorderlessFlatWindow.Windows.Checks/ # 原生透明测试窗口的集成检查
scripts/                             # 构建、检查与运行
docs/
  architecture.md
  roadmap.md
  acceptance.md
  adr/0001-native-windows-foundation.md
```

## 文档入口

- [TODO](TODO.md)：按里程碑和依赖排序的开发清单。
- [产品需求与范围](windows-spatial-canvas-requirements.zh-CN.md)：当前需求基线、优先级和功能语义。
- [架构与坐标约定](docs/architecture.md)：模块边界、事件流与原生接管前置条件。
- [窗口身份与准入](docs/window-discovery.md)：T01 策略、原生读取、验证证据与边界。
- [显示器与坐标边界](docs/display-coordinates.md)：T02 坐标契约、DPI 读取、越界检查与实测范围。
- [事件同步与真实地图](docs/window-lifecycle.md)：T03 批次、销毁失效、重入、移动回调协议与验证。
- [恢复日志与独立入口](docs/recovery.md)：T04 登记协议、崩溃恢复、命令、验证与边界。
- [真实画布管理](docs/managed-canvas.md)：T06 接管、连续平移、聚焦、暂停恢复与实测范围。
- [全局导航](docs/global-navigation.md)：T07 模式、快捷键、手势历史、线程与输入释放边界。
- [空间总览](docs/spatial-overview.md)：T08 稳定投影、搜索/选择、确认/取消、焦点保护与生命周期。
- [总览预览与降级](docs/overview-previews.md)：T09 资源生命周期、离屏标题占位、失败重试和真实画面验证。
- [位置标签与存储](docs/location-bookmarks.md)：T10 命名位置、重名区分、真实跳转、重启及文件损坏处理。
- [运行中新窗口接入](docs/runtime-window-admission.md)：FR-02 手动/自动接入、空位放置、失败恢复及五窗故障演练。
- [可试用原型交付检查](docs/prototype-plan.md)：持续目标的实际功能与验收边界。
- [开发路线图](docs/roadmap.md)：从 M0 骨架到真实桌面接管的顺序。
- [验收与验证记录](docs/acceptance.md)：模型检查、UI 冒烟检查和后续桌面验收。
- [技术选型记录](docs/adr/0001-native-windows-foundation.md)：为什么选择 .NET/WinForms/Win32。
- [离屏策略决策](docs/adr/0002-bounded-offscreen-parking.md)：有界停放、DWM 旧帧/裁剪空白与支持边界。
- [早期可行性调研](windows-canvas-feasibility.zh-CN.md)：包含已不在首版范围内的应用窗口缩放研究，作为历史资料保留。

`tests/BorderlessFlatWindow.Checks` 是控制台检查程序，不是 VSTest 项目；请用 `scripts/check.ps1`，不能以 `dotnet test` 的退出结果代替运行模型检查。

项目当前未选择发布许可证，也未复制 vxwm/Halley 的实现代码。正式分发和引入第三方代码之前需确定对应许可证。
