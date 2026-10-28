# 检查入口与证据范围

使用 Windows、PowerShell 7（`pwsh`）与 .NET 10 SDK，在仓库根目录执行。自定义检查器以进程退出码判定成功；统一入口是 `scripts/check.ps1`，不以 `dotnet test` 代替。用户下载包的 CMD 启动器仍可使用系统自带 Windows PowerShell，无需 SDK。

| 层次 | 命令（Release） | 范围与环境 |
| --- | --- | --- |
| Core | `./scripts/check.ps1 -Configuration Release` | 结构守卫、纯模型/状态协调、存储、隔离输入和隐藏接收器、启动器与包校验；默认 CI |
| UI | `./scripts/check.ps1 -Configuration Release -Layer Ui` | 独立 App.Checks 宿主的隐藏 UI 和合成场景；独立 CI 任务，不安装全局输入钩子或管理用户窗口 |
| Desktop | `./scripts/check.ps1 -Configuration Release -Layer Desktop -SmokeUi` | 自有原生窗口、DWM/前台与总览集成；需交互桌面，执行前确保正常画布不会自动接管测试窗口 |

原有 `-SmokeUi`、`-NativeChecks`、`-NativeOverviewChecks`、`-NativeBackdropChecks` 保留为显式组合；`-LiveSurfaceChecks` 调用独立 Lab。脚本分别输出 `layer=core/ui/desktop`，不得把某一层通过扩写为物理设备、GPU 或长期资源验收完成。

启动请求、交互资格和触控进入代号已提取到 Application，在默认模型检查中覆盖等待/取消、明确暂停、输入/恢复保护、短读取、旧回调、重复回调和新请求。UI 层继续验证控件与这些规则的连接。

## 单一源码的 UI 宿主

`tests/BorderlessFlatWindow.App.Checks` 链接正式 App 的界面源码（排除正式 Program、bin、obj），加入仅测试目录中的 partial 夹具与入口。这样可保留私有状态的重入回归，避免复制一套界面实现，也不把断言编入用户程序。资源使用固定逻辑名称，项目引用与版本来自同一构建规则；守卫还比较两者实际求值的框架、WinForms、DPI 模式与资源命名空间，防止测试配置漂移。这个宿主有自己的程序集身份，不能声称它是正式 App 的逐字节测试；编译清单守卫及包内真实 EXE 检查补充对应边界。

CI 的 Core 和 UI 任务均会编译完整解决方案。真实桌面、物理触控板/远控、系统锁屏/睡眠、拔插、混合 DPI、实际呈现延迟和长期资源仍按[验收清单](release-v1-readiness.md)执行；不上传桌面图片、窗口身份、运行报告或本地数据。

## 实验与测量

```powershell
dotnet run --project tools/BorderlessFlatWindow.Lab -c Release -- touchpad
dotnet run --project tools/BorderlessFlatWindow.Lab -c Release -- overview artifacts/lab/native.json
./scripts/measure-overview.ps1
```

实验输出只留在忽略目录。正式 App 遇到旧检查/实验参数会在初始化桌面接管前拒绝，不会误启动画布。包验证使用的 `--runtime-check` 只预热运行时与呈现依赖，不建立管理会话或输入钩子。

导航空闲和隐藏诊断的可重复测量使用 `./scripts/measure-navigation.ps1 -Label <新标签>`，输出仅保存在 `.local/performance/`；具体范围、前后结果及沙箱命名管道限制见[导航性能记录](navigation-performance.md)。单独运行新增调度回归可使用 App.Checks 的 `--navigation-check artifacts/navigation-check`；它仍属于完整 Ui 层的一部分。

工作区窗口复原另有[自有 A/B 程序检查](workspace-layouts.md)，入口为 Windows.Checks 的 `--workspace-only <新的 ignored 目录>`。`WorkspaceSource` 仅为开发夹具，不进入发行载荷；它在两分钟内自动退出，检查器正常结束时关闭自己的子进程。完整 Ui 层使用可注入启动端口，不打开用户软件。

工作区控制器和成员编辑器可单独运行：完整构建后，使用 `dotnet run --project tests/BorderlessFlatWindow.App.Checks -c Release --no-build -- --workspace-check <新的 ignored 目录>`。该合成夹具覆盖原样参数编辑、关闭自动启动、移除成员/删除工作区不关闭窗口、导航阻断与系统切换取消；它也包含在完整 Ui 层中。
