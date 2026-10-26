# 便携部署、启动与移除

当前仍为 Windows x64 私有内部试用。无需安装服务、开机启动或管理员权限。正常启动默认注册当前用户的软件 `.lnk` 右键便签入口，可在“更多 → 软件快捷方式右键入口”关闭并移除。应用依旧使用本机用户数据目录；“便携”指运行时随包携带，不表示把隐私数据写进程序目录。

## 两种包

| 模式 | 目标机器需要什么 | manifest |
| --- | --- | --- |
| 框架依赖版 | 已安装 .NET 10 Windows Desktop Runtime x64；仅普通 .NET Runtime 不包含桌面框架 | `frameworkDependent: true`，`requiredRuntime` 标明需求 |
| 自包含便携版 | 支持 .NET 10 的 Windows x64，运行时已经包含；无需 .NET 安装或 SDK | `frameworkDependent: false`，`deployment: self-contained`，`bundledFrameworks` 记录实际补丁版本 |

构建后发布采用[微软支持的自包含模式](https://learn.microsoft.com/en-us/dotnet/core/deploying/)。保留多文件目录，不启用裁剪、单文件自解压或 AOT。便携版主程序在根目录，独立恢复工具在 `tools/recovery/`，测试窗口在 `tools/probe/`，各自保留完整运行时；框架依赖小包仍为原有扁平目录。已实际遇到相同 10.0.13 版本的控制台与桌面运行时含有不同内容的同名 DLL，因此不混合覆盖。全部工具的框架补丁与 manifest 核对，主程序也能直接找到新目录中的恢复 EXE。

```powershell
# 原有小包
./scripts/package-trial.ps1 -NativeChecks -NativeOverviewChecks

# 自包含目录；显式指定已确认可用的 10.0 补丁版本
./scripts/package-trial.ps1 -SelfContained -RuntimeVersion 10.0.13 -NativeChecks -NativeOverviewChecks
```

省略 `RuntimeVersion` 时由构建 SDK 选择运行时补丁；不声称它一定是最新版本。便携包使用自身携带的运行时，不会因为系统更新共享 .NET 而自动换版；维护时应选择补丁、重新构建并复验。开发者恢复运行时包可能需要网络，生成后的便携包启动无需为此联网。

全部解压到一个新文件夹，双击 `Start.cmd`。`Start-NativeOverview.cmd` 明确启用原生总览，`Start-FormOverview.cmd` 回退窗体总览，`Probe.cmd` 打开测试窗口；相同入口兼容两种目录布局。也可直接运行对应 EXE；启动器不安装依赖、不修改系统环境变量或保存启动日志。

## 启动与恢复错误

启动入口使用系统自带 Windows PowerShell。它先检查 EXE/配置是否存在，再调用程序自身的 .NET apphost；不通过注册表或 PATH 猜测运行时是否兼容。仅对子进程设置[关闭 apphost 依赖错误弹框](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_disable_gui_errors)，使缺失依赖以退出码和提示显示；不改变应用的异常处理。

GUI 启动器仅观察最初 1.8 秒内的退出；进程仍运行时便交还控制，不终止、不重启，也不据此证明完整界面初始化成功。缺文件时要求完整解压；框架依赖版提示 Desktop Runtime 或换便携版；便携版损坏提示重新完整解压。失败时 CMD 保持提示可读并保留退出码。诊断只在本地控制台显示，不自动上传。

`Recover.cmd` 和 `Inspect-Recovery.cmd` 等待独立恢复工具完成并显示输出，保留退出码：0 完成/检查成功，2 尚待恢复，1 操作错误，其他主机失败码原样传回。检查命令返回 0 只表示日志读取成功，应继续核对 `pending` 是否为 0。损坏日志的操作错误会提示检查恢复输出，不误导为安装运行时。需要指定其他日志时：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./Launch.ps1 -Mode Inspect -JournalPath 'C:\path\recovery.json'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./Launch.ps1 -Mode Recover -JournalPath 'C:\path\recovery.json'
```

若当前目录脚本无法运行，可直接使用恢复 EXE 的 `--inspect --journal` / `--journal` 入口（便携版位于 `tools/recovery/`）。恢复日志仍受实例租约、窗口身份和标记核验保护，启动器不绕过这些检查。

## 更新与移除

先在旧版暂停并恢复，再从托盘退出，待原实例结束后把新版解压到不同目录并启动。不要覆盖运行中的文件。设置、工作区和恢复记录仍位于 `%LOCALAPPDATA%\BorderlessFlatWindow`，新包不会包含这些数据；更新不自动开始接管。

软件快捷方式入口仅位于 `HKCU\Software\Classes\lnkfile\shell\BorderlessFlatWindow.Sticky`，带应用所有权标记，不修改默认关联。升级时新目录中的程序会更新入口路径。移除前先关闭该入口选项，再完成恢复和退出，通过文件资源管理器删除应用目录。若已删除旧目录，可用有效包关闭该选项以移除残留入口；没有服务需要卸载，详情见[便签说明](pointer-and-sticky.md)。

默认保留用户数据。若也要清理数据，先检查 `pending: 0`，按需备份工作区和设置，再由用户处理对应目录；待恢复或损坏而尚未处理的日志不作为缓存清除。这里没有自动删除或卸载脚本。

## 许可与验收

`scripts/verify-trial.ps1 -Directory <目录> -Archive <ZIP> -RequireClean -RejectLocalBuildPaths` 核对源码标识、全部文件与 ZIP 的长度/哈希、入口、运行配置和许可材料，拒绝额外文件、用户运行数据、PDB、越界/重复路径及重解析点，并检查二进制中的本机构建与用户目录。打包脚本自动启用路径检查；哈希清单证明文件一致性，不是发布者数字签名。

打包在完成检查后清理 Release 输出，再以无调试符号及固定源路径映射重新发布，避免复用检查阶段依赖 DLL 内的绝对 CodeView/PDB 路径。普通开发构建继续保留调试能力；旧包诊断可以省略新增的路径检查开关，但新生成包必须通过该检查。

源码修改标记同时考虑已跟踪改动与源码/测试/脚本/文档/许可目录和构建配置内未跟踪的输入，避免新代码尚未加入 Git 却被标为干净版本。与构建无关的项目根目录未跟踪文件不进入包或这一标记。

随包组件和许可来源见 `THIRD-PARTY-NOTICES.md`。SDK 分发原文和自包含运行时包的对应版本 LICENSE/NOTICE 随包保留，并进入哈希清单；不把这些条款套用到项目代码。

默认 CI 增加真实 Windows PowerShell 启动器检查：空日志检查/恢复、损坏数据和原字节保留，空格/符号/中文路径，六个 CMD 的失败退出，以及合成缺失/损坏运行时。校验器覆盖实际编译的框架依赖夹具和合成的独立运行时布局，拒绝缺件/越界/隐私数据和跨工具版本不符。检查仅使用独立夹具，无桌面接管。

便携包同时指定 `-NativeOverviewChecks` 时，还会自动执行 `scripts/check-portable.ps1`：直接运行包内 EXE，在自身进程中核验 CoreCLR/host/WPF 的模块位置，再完成原生总览、真实独立恢复桥接和恢复启动器。仅对子进程把 DOTNET_ROOT 指向不可用位置，不移除或修改机器已安装的运行时。恢复检查只使用独立合成记录，重复使用非空夹具目录会拒绝且保留文件；`portableChecksRun` 仅在这些发布后检查成功后置为 true。

首次完整原生回归曾出现预览测试“未找到元素”，原日志只有消息，无法区分它来自单次全桌面身份查找，还是失败诊断的另一处 Single。相关三处身份读取现改用相同目标的稳定准入等待，失败诊断也能表示缺失观察值；预览、裁剪及资源断言保持。八项预览/焦点检查连续三轮通过，完整 59 项原生检查随后通过。这记录为测试读取的加固，不证明所有夹具/文件替换偶发问题已消失，T11/T12 继续追踪。

这些证据尚不等于在真正未安装 .NET 的干净系统完成全部桌面验收；设备兼容、许可审查与正式发布仍按 T11–T13 继续。
