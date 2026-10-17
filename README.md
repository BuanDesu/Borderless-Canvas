# Borderless Flat Window

[GitHub 私有仓库](https://github.com/BuanDesu/Borderless-Flat-Window) · [开发任务](https://github.com/BuanDesu/Borderless-Flat-Window/issues) · [里程碑](https://github.com/BuanDesu/Borderless-Flat-Window/milestones)

Windows 原生的连续二维窗口画布：窗口保留空间位置，屏幕作为可移动视口；通过空间总览定位窗口。应用窗口保持原生比例。

当前进入 **M1 只读桌面映射**，本批实现 T01 窗口身份与准入策略。程序保留四个示例窗口的空间模型预览，并可检查真实 Windows 窗口的纳入/排除原因、所属窗口及稳定模型 ID。真实窗口移动、全局输入、DWM 缩略图和恢复日志尚未接入。

## 快速开始

开发环境：Windows、.NET 10 SDK（含 Windows Desktop 引用包）。无需 WSL、Node.js 或第三方 NuGet 包。

在项目根目录的 PowerShell 中运行：

```powershell
# 编译整个解决方案
.\scripts\build.ps1

# 执行空间模型检查；加 -SmokeUi 可创建离屏窗口并渲染一张检查图
.\scripts\check.ps1 -SmokeUi

# 在具备交互桌面的 Windows 上检查透明原生测试窗口
.\scripts\check.ps1 -Configuration Release -SmokeUi -NativeChecks

# 启动桌面预览程序
.\scripts\run.ps1
```

也可以直接用 SDK：

```powershell
dotnet build BorderlessFlatWindow.slnx
dotnet run --project tests/BorderlessFlatWindow.Checks
dotnet run --project src/BorderlessFlatWindow.App
```

脚本将 CLI 临时状态写入忽略目录 `.local/`，并关闭与本项目无关的工作负载探测。若受限环境中的默认构建遇到 SDK 探测问题，优先使用脚本。`global.json` 允许已安装的 .NET 10 稳定版 SDK feature band。

## 当前可以操作什么

- 在“空间模型预览”中点击矩形，将模型相机定位到该窗口。
- 使用箭头按钮或滚轮移动相机；Shift＋滚轮横移。
- Home 返回原点；Backspace 返回上一视角。预览中的虚线是视口，十字是原点。
- 在“Windows 窗口候选（只读）”中点击“检查窗口”，查看可纳入的窗口与稳定模型 ID。
- 勾选“显示被排除的窗口”查看原因，例如自身、桌面/任务栏、工具/所属窗口、DWM cloak、其他虚拟桌面、最小化或无法验证的状态。
- 窗口重命名和移动不更换已验证身份；刷新失败保留上次结果。当前手动检查，不代表已经接入生命周期事件。

预览不是桌面覆盖层；示例矩形不是被接管的应用。当前不会移动、激活、隐藏或最小化其他应用。预览为了展示基础模型，每次绘制会适配示例窗口与视口范围；冻结总览比例、搜索、书签 UI、键盘方向选择等见后续里程碑。

## 目录

```text
BorderlessFlatWindow.slnx
src/
  BorderlessFlatWindow.Core/          # 纯几何、相机、总览投影与领域记录
  BorderlessFlatWindow.Application/   # 画布会话、导航历史、位置书签、平台端口
  BorderlessFlatWindow.Windows/       # Win32 只读窗口枚举；后续接入移动与预览
  BorderlessFlatWindow.App/           # WinForms 入口与模型预览
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
- [开发路线图](docs/roadmap.md)：从 M0 骨架到真实桌面接管的顺序。
- [验收与验证记录](docs/acceptance.md)：模型检查、UI 冒烟检查和后续桌面验收。
- [技术选型记录](docs/adr/0001-native-windows-foundation.md)：为什么选择 .NET/WinForms/Win32。
- [早期可行性调研](windows-canvas-feasibility.zh-CN.md)：包含已不在首版范围内的应用窗口缩放研究，作为历史资料保留。

`tests/BorderlessFlatWindow.Checks` 是控制台检查程序，不是 VSTest 项目；请用 `scripts/check.ps1`，不能以 `dotnet test` 的退出结果代替运行模型检查。

项目当前未选择发布许可证，也未复制 vxwm/Halley 的实现代码。正式分发和引入第三方代码之前需确定对应许可证。
