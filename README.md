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

# 测量自有 1/10/30/60 窗口的总览与原生预览负载，不抓取桌面
.\scripts\measure-overview.ps1

# 启动桌面画布程序
.\scripts\run.ps1

# 独立检查上次会话的恢复日志；去掉 -Inspect 才会恢复已验证窗口
.\scripts\recover.ps1 -Configuration Release -Inspect

# 运行自己的测试窗口，验证停放、预览、恢复与原生输入
dotnet run --project tests/BorderlessFlatWindow.Probe --configuration Release

# 检查后生成包含 GUI、独立恢复、测试窗口及中文说明的试用目录和 ZIP
.\scripts\package-trial.ps1 -NativeChecks

# 自带运行时的 Windows x64 便携包
.\scripts\package-trial.ps1 -SelfContained -RuntimeVersion 10.0.13 -NativeChecks -NativeOverviewChecks
```

也可以直接用 SDK：

```powershell
dotnet build BorderlessFlatWindow.slnx
dotnet run --project tests/BorderlessFlatWindow.Checks
dotnet run --project src/BorderlessFlatWindow.App
```

脚本将 CLI 临时状态写入忽略目录 `.local/`，并关闭与本项目无关的工作负载探测。若受限环境中的默认构建遇到 SDK 探测问题，优先使用脚本。`global.json` 允许已安装的 .NET 10 稳定版 SDK feature band。

多窗口预览同步现在复用同一批次的虚拟桌面查询服务，并保留逐窗核验；计时拆分和本机负载数据见[总览负载基线](docs/overview-load-baseline.md)。它不代表真实输入延迟或实际呈现帧率已验收。

试用目录无需源码或 SDK，双击 `Start.cmd` 即可。框架依赖版需 .NET 10 Windows Desktop Runtime x64，自包含便携版携带运行时；模式与补丁版本记录在 manifest。启动入口提供依赖/缺文件提示，恢复脚本保留退出码；目录和 ZIP 自动核验，调试符号及用户运行数据不入包。升级、移除和许可来源见[部署说明](docs/deployment.md)与[第三方清单](THIRD-PARTY-NOTICES.md)，生成物位于 ignored `artifacts/trial/`。

Windows 11 build 22621 及以上默认使用无桌面位图读回的原生总览，较早系统保留窗体总览。用户远控体验已确认无明显卡顿或色块刷新。兼容问题可先退出再用 `Start-FormOverview.cmd` 回退；`Start-NativeOverview.cmd` 保留显式启用入口。完整矩阵仍在验收，详见[原生总览](docs/native-overview-presenter.md)。

## 当前可以操作什么

正式版所需的功能、设备和发布条件集中在[首版验收清单](docs/release-v1-readiness.md)。当前仍是内部试用，已通过的用户体验和自动检查与剩余工作分别列明。

- 点击“开始”接管当前全部可用普通窗口，便签保持独立；启动程序本身仍不自动接管。
- 新开的普通窗口默认自动接入，在当前视角附近找空位；可以关闭自动接入，或手动加入候选。
- 鼠标在桌面空白处自动导航：滚轮纵移、Shift＋滚轮横移、左右双键拖动；进入应用窗口直接操作应用。Home 原点，Backspace 上一段视角，Esc 暂停自动导航。
- 桌面空白处 Tab / Shift+Tab 打开总览，总览内同样按键返回；Ctrl+Alt+O 为备用入口。方向键按空间选择，Ctrl+Tab 轮选、标题搜索，Enter / 单击定位，Esc 同样取消。
- 桌面 Ctrl＋滚轮以快速镜头过渡轮选并聚焦窗口；应用内 Ctrl＋滚轮仍由应用处理。
- 鼠标抵住屏幕外缘后继续向外推动可平移；Ctrl+Alt+C 进入独立中心准星模式，鼠标位移持续移动镜头，单击不退出；Esc 或再次按组合键退出。准星自动适配相对、绝对和普通坐标输入；具体远控仍需复验，边缘推进仍依赖相对输入，详见[启动与远控修正](docs/startup-and-remote-input.md)。
- Ctrl+Alt+P 或窗口标题栏右键切换便签：固定当前屏幕位置，保留原层级。软件 `.lnk` 右键可“以画布便签模式打开”；新建窗口识别及限制见[指针导航与便签](docs/pointer-and-sticky.md)。
- 总览上方地图保留相对位置，下方 CRT 工作区显示真实视窗内容并保存视角，支持新增、筛选、重命名、删除和跳转。工作区无固定槽位，标签数据兼容原有存储。
- “导航设置”配置两轴滚轮、双键手势、距离和手动导航备用快捷键；这些选择及自动接入、取色和动画开关保存在本机。“更多 → 本地设置”可重新读取或重试保存，失败时仅本次生效并保留原文件，见[本地设置](docs/local-preferences.md)。
- “更多 → 整理”提供可勾选的平铺预览与一次撤销；保留原生尺寸、避开未选画布窗口，空间不足向下延伸，相机与工作区不变。详见[区域整理](docs/region-arrangement.md)。
- “设备诊断”整合窗口候选、显示器与坐标、真实空间地图三个子页。开发模型示例不在正常界面出现。
- 同 DPI 的多屏共享连续画布，聚焦目标回主屏。混合/未知 DPI 仍明确拒绝；最大化、全屏和置顶等不支持的状态需要先解除。
- 锁屏、断连、电源或显示配置变化时先关闭画布输入，再安全暂停；重新读取设备后保留世界布局、相机历史和工作区，等待手动开始。读取失败禁止沿用旧配置，见[系统切换](docs/desktop-lifecycle.md)；真实睡眠、拔插和更换主屏仍待硬件验收。
- 关闭或最小化管理器收入托盘，画布继续运行；双击图标返回。悬停及菜单显示运行/暂停/待恢复状态，菜单可直接打开设置和恢复中心；退出时恢复运行窗口，待恢复记录保留并明确提示，见[托盘入口](docs/tray-control.md)。

原生总览由系统合成背景，不读取桌面位图；旧窗体路径的背景仅在内存中模糊，关闭释放。预览可能停帧或降级为标题卡片。真实多屏、广泛应用、性能和正式发布矩阵尚未全部验收，具体边界见[改进说明](docs/interaction-refresh.md)。

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
- [指针导航与便签](docs/pointer-and-sticky.md)：默认全量接管、边缘推进、准星、镜头轮切、标题栏及软件快捷方式固定。
- [空间总览](docs/spatial-overview.md)：T08 稳定投影、搜索/选择、确认/取消、焦点保护与生命周期。
- [总览预览与降级](docs/overview-previews.md)：T09 资源生命周期、离屏标题占位、失败重试和真实画面验证。
- [无桌面读回的原生宿主实验](docs/live-overview-surface.md)：独立的系统背景与实时矩形过渡验证历史。
- [完整总览的原生适配](docs/native-overview-presenter.md)：共享地图/工作区视图、真实 HWND 与输入/恢复接入，按系统版本默认选择并保留显式回退。
- [原生背景配置与合成验证](docs/native-backdrop-validation.md)：修正无边框宿主的非客户区渲染策略，以自有图案验证透明模糊和系统降级。
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
