# 第三方组件与随包原文

项目代码和原创图标、SVG 采用根目录的 [MIT 许可证](LICENSE)，版权署名为 BuanDesu。下列第三方条款各自适用于对应组件，不由本项目的 MIT 许可证替代。源码公开不改变第三方许可条件，也不表示完整的二进制分发审查已经完成。

| 组件 | 来源与版本 | 随包原文 |
| --- | --- | --- |
| Fusion Pixel 简中字体 | [上游](https://github.com/TakWolf/fusion-pixel-font)，2026.10.16，未修改嵌入；SHA-256 见原 NOTICE | `Assets/Licenses/FusionPixel/NOTICE.md`、`OFL.txt` 及 `LICENSES/`，含上游来源字体的条款 |
| .NET 本机 apphost 与分发组件 | Microsoft .NET SDK 生成；SDK 版本记录在 manifest | `licenses/dotnet-distribution/LICENSE.txt`、`ThirdPartyNotices.txt` 从实际构建 SDK 分发根目录原样复制；另保留 .NET Foundation MIT 原文 `MIT-LICENSE.TXT` |
| 自包含 .NET / Windows Desktop 运行时 | 仅便携版包含；具体框架及补丁版本见 manifest 的 `bundledFrameworks` | `licenses/microsoft.netcore.app.runtime.win-x64/<version>/` 与 `licenses/microsoft.windowsdesktop.app.runtime.win-x64/<version>/`，从相同版本的官方 NuGet 运行时包复制 LICENSE 与存在的 THIRD-PARTY-NOTICES |

.NET Windows 分发包含多种条款，不能把整个运行时简单归为 MIT。微软的[Windows 许可清单](https://github.com/dotnet/core/blob/main/license-information-windows.md)列出受 [.NET Library License](https://dotnet.microsoft.com/en-us/dotnet_library_license.htm) 和 [Windows SDK 条款](https://learn.microsoft.com/en-us/legal/windows-sdk/license)约束的本机组件，其余组件及具体第三方来源应结合随包原文阅读。NuGet 包中的 MIT LICENSE 与 Windows 分发条款均保留，不互相替代。

框架依赖版由目标机器提供共享运行时，便携版分发自己的运行时。项目没有额外第三方 NuGet 库；SDK 的框架引用/运行时包不计为项目自选库。SDK 自身的开发工具不随试用包分发。生成包的所有许可文本与其他载荷一样进入 SHA-256 manifest。

这份清单记录当前来源与保留材料。项目已选用 MIT；全部第三方条款的发布审查、签名及安装器继续按 T13 跟踪；便携试用不等于这些发布条件已经完成。

仓库内 `licenses/dotnet/MIT-LICENSE.TXT` 原样取自官方 `Microsoft.NETCore.App.Runtime.win-x64` 10.0.13 的 LICENSE.TXT，SHA-256 为 `D7A68596AB69B06F51CA278A6545148E4269A9381C26D597C13DF5D88E08CF5B`；相应[上游许可](https://github.com/dotnet/runtime/blob/v10.0.13/LICENSE.TXT)适用于 .NET 组件。
