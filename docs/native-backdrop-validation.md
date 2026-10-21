# 原生总览背景：配置修正与合成验证

完整原生总览的背景曾在接口返回成功时仍呈纯色。本轮在相同宿主上用自有图案做对照，定位到无边框 WPF 宿主的 DWM 非客户区渲染未启用。修正后，激活的生产宿主能显示实际透明模糊背景；默认启动路径仍不切换，连续过渡与自然操作继续验收。

## 最小修正

`NativeOverviewBackdrop.TryApply` 仍只接受当前进程的顶层 HWND，在 Windows 11 build 22621 起请求系统背景。现在先设置 `DWMWA_NCRENDERING_POLICY = DWMNCRP_ENABLED`，再扩展全客户区玻璃框架、设置 Desktop Acrylic，并读取 `DWMWA_NCRENDERING_ENABLED`。`Accepted` 同时要求各 HRESULT 成功、背景类型正确和非客户区渲染已启用。它表示配置被接受，不能单独证明视觉效果。

[非客户区策略文档](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmncrenderingpolicy)说明启用策略独立于窗口样式；[窗口属性文档](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)区分设置策略、读取实际状态和设置背景类型。现有透明 Window、透明 HwndTarget 和框架扩展保持不变，参见 [WPF 玻璃框架示例](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/extend-glass-frame-into-a-wpf-application)。这里的具体根因来自本机对照，不能据此推断全部无边框应用都必然如此。

对照还尝试了 build 26100 起的 `DWMWA_REDIRECTIONBITMAP_ALPHA`：单独启用它无改善，与非客户区策略同时启用也未增加本机收益。因此生产代码没有加入这个属性或提高最低背景支持版本。

## 可重复诊断

```powershell
./scripts/check.ps1 -Configuration Release -SmokeUi -NativeChecks -NativeOverviewChecks -NativeBackdropChecks
```

背景诊断也可以独立运行，报告路径必须尚不存在：

```powershell
./src/BorderlessFlatWindow.App/bin/Release/net10.0-windows/BorderlessFlatWindow.App.exe --native-backdrop-check artifacts/backdrop-new.json
```

该命令不接管外部窗口，创建自有不透明图案底窗和实际共享总览视图，依次绘制两种纯色及 16px 条纹。每个阶段等待合成后，只对总览内部 192×32px 区域计算均值差和条纹衰减；原始像素不写盘、不加入报告、不上传。30 秒看门狗限制运行时间，Esc 或关闭按钮取消。

读回前后均核对：两个窗口属于本进程、顶层且非分层、可见、几何范围完全匹配，底窗覆盖目标、目标包含采样区；有界枚举可见窗口顺序，其他窗口覆盖采样区或状态未知时拒绝采样。前后核验不是原子桌面快照，不能消除期间短暂插入又移除的遮挡。这个有界桌面读回只存在于显式诊断命令，原生生产总览仍不读取桌面位图。

十组对照保留旧实现以避免测试只重复修正后的逻辑：纯色、主动遮挡、主动移动、旧实现未激活/激活、旧实现加非客户区策略、旧实现加 alpha、旧实现同时加两项、生产实现激活/未激活。遮挡和移动组必须零读回；若有效正对照有响应和衰减，而同样有效且激活的生产实现丢失效果，则检查失败。

`ProductionEffectVerified=true` 要求有效的生产样本、实际激活且为前台、非客户区渲染启用、背景响应达到阈值、细条纹显著衰减。条件不足打印 `INCONCLUSIVE`；退出码 0 只代表诊断没有违反断言，不能把它解释为材质验证成功。取消返回 2。报告只包含标量、布尔值、HRESULT、状态分类及只读环境能力；没有窗口标题、路径、进程或窗口标识。

## 本机证据

全量回归中的匿名结果如下。响应是两种自有纯色引起的 RGB 平均绝对差；比值是条纹对比差除以该响应，属于诊断量，不是光学模糊半径或跨设备性能指标。

| 对照 | 非客户区渲染 | 背景响应 | 条纹/响应 |
| --- | --- | ---: | ---: |
| 纯色控制 | 关闭 | 0 | 不适用 |
| 旧实现激活 | 关闭 | 0 | 不适用 |
| 旧实现仅加 alpha | 关闭 | 0 | 不适用 |
| 旧实现加非客户区策略 | 开启 | 62 | 约 0.000896 |
| 旧实现同时加两项 | 开启 | 62 | 约 0.000896 |
| 修正后生产实现激活 | 开启 | 62 | 约 0.000896 |
| 修正后生产实现未激活 | 开启 | 0 | 不适用 |

旧实现未激活也为零响应。遮挡与移动控制均拒绝采样，各零次读回。`ProductionEffectVerified=true`，细条纹对比差约 0.0556。激活状态不能忽略：[Acrylic 文档](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic)说明背景 Acrylic 在窗口失活时会退为纯色，透明效果关闭、节电、高对比度等状态也可能降级。本轮没有改变任何系统设置。

309 项原有模型/存储/原生检查、默认 UI/场景及完整原生 presenter 检查通过；后者追加检查被接受的背景在显示后仍保持非客户区渲染开启。匿名运行指标新增该策略的设置/读取结果，以及首个动画回调的实际激活/前台状态；缺失状态写 null。

真实管理器关闭自动接入、仅接管自有 Source 后，工具画面已显示模糊背景、实时地图及 CRT 内容；地图选窗、备用快捷键重开、Tab 返回、暂停恢复通过，恢复日志 pending/armed 均为零。工具截图与原始报告只留在本地。此次没有把截图或回调间隔当作连续呈现或物理快速输入的流畅度证据，也没有补验真实工作区模态编辑、混合 DPI、长期资源与全部兼容矩阵。

## 试用与后续

先退出正在运行的管理器并恢复窗口，再从新试用包的 `Start-NativeOverview.cmd` 启动。默认 `Start.cmd` 保留原路径；`--native-overview-solid` 可强制纯色。打包脚本支持 `-NativeBackdropChecks` 并在 manifest 中单独记录是否运行，报告不进入包。

这次修正解决了当前配置下的纯色根因；自然快速反向、焦点缩小资格、多窗口及实际屏幕连续呈现仍在 #41 / T11 / T12 跟踪，不以背景修正关闭这些项目。
