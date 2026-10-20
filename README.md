# Borderless Flat Window

[GitHub 私有仓库](https://github.com/BuanDesu/Borderless-Flat-Window) · [开发任务](https://github.com/BuanDesu/Borderless-Flat-Window/issues) · [里程碑](https://github.com/BuanDesu/Borderless-Flat-Window/milestones)

Windows 原生的连续二维窗口画布：窗口保留空间位置，屏幕作为可移动视口；通过空间总览定位窗口。应用窗口保持原生比例。

**Fluent 糖果控制台**：右下角竖向弹窗、壁纸取色、像素品牌字与对齐的游戏机按键；Tab 往返总览，地图保留所有可用窗口预览，CRT 工作区显示对应视窗的真实内容。WPF 合成层取代逐帧整屏 CPU 混合，DWM 继续负责真实窗口预览。参考 vxwm 的连续空间交互，具体取舍、性能数据和验收边界见 [Fluent 控制台与合成层过渡](docs/fluent-motion.md)及[试用指南](docs/trial-guide.zh-CN.md)。

## 快速开始

本轮总览重构：保留空间关系，以单个 WPF 场景承载地图、搜索和工作区，移除运行时旧控件树、整页截图和整屏 CPU 模糊；DWM 继续提供真实内容。输入会话隔离旧回调，确认选窗后立即交接。原生任务视图的能力边界、实现和测量限制见[总览渲染重构](docs/retained-overview.md)。主面板保留已通过的样式。

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

# 检查后生成包含 GUI、独立恢复、测试窗口及中文说明的试用目录和 ZIP
.\scripts\package-trial.ps1 -NativeChecks
```

也可以直接用 SDK：

```powershell
dotnet build BorderlessFlatWindow.slnx
dotnet run --project tests/BorderlessFlatWindow.Checks
dotnet run --project src/BorderlessFlatWindow.App
```

脚本将 CLI 临时状态写入忽略目录 `.local/`，并关闭与本项目无关的工作负载探测。若受限环境中的默认构建遇到 SDK 探测问题，优先使用脚本。`global.json` 允许已安装的 .NET 10 稳定版 SDK feature band。

试用目录无需源码或 SDK，双击 `Start.cmd` 即可；需已安装 .NET 10 Windows Desktop Runtime x64。`Recover.cmd` 独立恢复，`Inspect-Recovery.cmd` 仅检查。`manifest.json` 记录版本和文件 SHA-256；包不含运行时，生成物位于 ignored `artifacts/trial/`。

## 当前可以操作什么

- 在“窗口画布”勾选普通窗口并开始；开始前已有的未选窗口保持排除。
- 新开的普通窗口默认自动接入，在当前视角附近找空位；可以关闭自动接入，或手动加入候选。
- 鼠标在桌面空白处自动导航：滚轮纵移、Shift＋滚轮横移、左右双键拖动；进入应用窗口直接操作应用。Home 原点，Backspace 上一段视角，Esc 暂停自动导航。
- 桌面空白处 Tab / Shift+Tab 打开总览，总览内同样按键返回；Ctrl+Alt+O 为备用入口。方向键按空间选择，Ctrl+Tab 轮选、标题搜索，Enter / 单击定位，Esc 同样取消。
- 桌面 Ctrl＋滚轮轮选并聚焦受管理窗口；应用内 Ctrl＋滚轮仍由应用处理。
- 总览上方地图保留相对位置，下方 CRT 工作区显示真实视窗内容并保存视角，支持新增、筛选、重命名、删除和跳转。工作区无固定槽位，标签数据兼容原有存储。
- “导航设置”配置两轴滚轮、双键手势、距离和手动导航备用快捷键。设置目前只在本次进程有效。
- “设备诊断”整合窗口候选、显示器与坐标、真实空间地图三个子页。开发模型示例不在正常界面出现。
- 同 DPI 的多屏共享连续画布，聚焦目标回主屏。混合/未知 DPI 仍明确拒绝；最大化、全屏和置顶等不支持的状态需要先解除。
- 关闭或最小化管理器收入托盘，画布继续运行；双击托盘图标返回。暂停恢复原位；托盘“退出并恢复窗口”才真正退出。异常退出可用“恢复中心”或独立恢复程序。

总览桌面背景仅在内存中模糊，关闭释放；预览可能停帧或降级为标题卡片。真实多屏、广泛应用、性能和正式发布矩阵尚未全部验收，具体边界见[改进说明](docs/interaction-refresh.md)。

## 目录

```text
BorderlessFlatWindow.slnx
src/
  BorderlessFlatWindow.Core/          # 纯几何、相机、总览投影与领域记录
  BorderlessFlatWindow.Application/   # 画布会话、导航历史、位置书签、平台端口
  BorderlessFlatWindow.Windows/       # Win32 发现、事件、受保护移动、恢复和预览
  BorderlessFlatWindow.App/           # WinForms 管理入口、空间地图与诊断
  BorderlessFlatWindow.Presentation/  # WPF 纹理合成与逐帧时钟
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
- [本地试用指南](docs/trial-guide.zh-CN.md)与[试用产物验证](docs/trial-validation.md)：启动、操作、恢复、兼容证据及剩余人工验收。
- [开发路线图](docs/roadmap.md)：从 M0 骨架到真实桌面接管的顺序。
- [验收与验证记录](docs/acceptance.md)：模型检查、UI 冒烟检查和后续桌面验收。
- [技术选型记录](docs/adr/0001-native-windows-foundation.md)：为什么选择 .NET/WinForms/Win32。
- [离屏策略决策](docs/adr/0002-bounded-offscreen-parking.md)：有界停放、DWM 旧帧/裁剪空白与支持边界。
- [早期可行性调研](windows-canvas-feasibility.zh-CN.md)：包含已不在首版范围内的应用窗口缩放研究，作为历史资料保留。

`tests/BorderlessFlatWindow.Checks` 是控制台检查程序，不是 VSTest 项目；请用 `scripts/check.ps1`，不能以 `dotnet test` 的退出结果代替运行模型检查。

项目当前未选择发布许可证，也未复制 vxwm/Halley 的实现代码。正式分发和引入第三方代码之前需确定对应许可证。
