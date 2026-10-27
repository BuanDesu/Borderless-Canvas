# Fluent 控制台与合成层过渡

> 历史记录：保留当时的实现与验证过程。当前规则以[架构](../architecture.md)、[检查分层](../testing.md)和[验收清单](../release-v1-readiness.md)为准。

用户已确认 后续验收 的按钮边缘、字体完整性、真实工作区内容与离屏预览；本轮继续处理过渡卡顿和布局质感。

## 参考与取舍

项目的空间交互灵感来自 [vxwm 原仓库](https://codeberg.org/wh1tepearl/vxwm)及其作者维护的 [GitHub 只读镜像](https://github.com/wh1tepearll/vxwm)。重新阅读了 `modules/infinitetags/infinitetags.c`、`modules/zoom/zoom.c` 和 `modules.def.h`：

- `movecanvas` / `movecanvasmouse` 为同一画布上的窗口统一改变屏幕坐标；鼠标移动保留小数余量，并可按刷新率限制请求。
- `centerwindow` 对整组窗口施加相同位移，使目标位于视口中心；`homecanvas` 按累计位移回到原点。这里借鉴空间关系与焦点规则。
- vxwm 是 X11 的 suckless dwm 分支。它不是 Windows Desktop Window Manager；缩放集成读取外部 vcompmgr 的状态，部分视觉过渡依赖外部合成器。本项目继续通过 Windows 原生窗口 API 和 DWM 工作，不能直接移植其 Xlib 或合成器代码。
- “infinite tags” 描述每个标签内的连续空间，不等于该实现提供任意数量的标签。本项目保存不限固定槽位的工作区视角，仍共享同一世界坐标。

检索并比较了 [WPF 自带 Fluent 主题](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90)、[WPF UI](https://github.com/lepoco/wpfui) 和微软 [Fluent 2 布局](https://fluent2.microsoft.design/layout) / [层级](https://fluent2.microsoft.design/elevation)。采用 Fluent 的 4px 间距体系、分组与视觉层级，保留现有原生控件的可访问性及窗口引擎；没有声称引入 WPF UI 控件，也没有复制 vxwm 源码或第三方模板素材。

## 视觉规则

- 珍珠白基底承载玻璃层，糖果色集中于主操作键、品牌显示区、地图与工作区焦点；壁纸取色仍决定强调色。
- 底部划分等宽两组；方向键居中，开始/暂停与总览使用相同尺寸、同一基线；上一视角和更多操作共享宽度、位置和高度。
- 正文使用 Segoe UI 与系统中文回退；像素字体留在品牌显示区及装饰中。正文不再承担像素装饰的任务。
- 移除大面积高对比外框、重复装饰和空地图中的多层套框；窗口选择列表保留完整的复选框，不能用圆角区域切掉首行内容。

## 动效实现

新增独立 `BorderlessFlatWindow.Presentation` 程序集，仅依赖已要求的 .NET 10 Windows Desktop Runtime；不增加下载型控件包。WinForms 管理器继续负责输入、恢复和原生窗口关系。

旧过渡在 16ms 的界面计时器中，用 GDI+ 每帧绘制桌面并混合整屏目标图。新路径只生成一次最终界面，将预乘 alpha 位图上传为冻结的 WPF `BitmapSource`，由合成线程做 280ms 淡入；DWM 缩略图的位置与透明度通过 `CompositionTarget.Rendering` 更新。结束前固定最终透明度，避免停止动画后回到起始值造成闪帧。取消总览时，原屏内焦点预览反向回到原来的屏幕位置。

启动时预热 WPF，正式动画前先呈现纹理，避免把首次纹理上传放进移动中的帧。屏内背景使用不缩放的预乘位图复制，生产路径不再执行逐帧 GDI+ alpha 混合。短暂过渡期间保留 WinEvent 队列和输入心跳，延后目录 COM 查询及隐藏诊断页重建；结束后正常消费队列，确认定位仍重新核验目标。壁纸小图以纯像素内存缓存供多次总览复用，桌面设置或显示器布局改变时失效。关闭或出错时取消静态逐帧订阅并释放宿主；软件渲染、系统关闭动画或关闭外观选项时直接打开/关闭总览。

依据：[WPF 渲染层级](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/graphics-rendering-tiers)、[CompositionTarget 逐帧回调](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-render-on-a-per-frame-interval-using-compositiontarget)。这些接口并不保证任何硬件都达到固定帧率。

## 可重复测量

`BorderlessFlatWindow.App --motion-check <本地输出文件>` 只生成合成色彩图，分别测量 1920×1080 与 3440×1440 的旧 CPU 混合成本、新合成回调间隔和释放行为，不抓取桌面。首次初始化耗时单独记录；空回调成本不能当作 GPU 呈现时间。

`--motion-metrics <本地输出文件>` 是可选的真实总览诊断，仅输出尺寸、DPI、渲染层级、预览数量、准备时间、帧回调间隔和原生更新耗时。默认不生成文件；不记录窗口标题、身份、壁纸、屏幕像素或本地来源路径。

本机合成画面基线：旧 CPU 混合的 P95 在 1080p 约 10ms、3440×1440 约 22ms。新路径移除此项逐帧 CPU 工作，但回调节奏仍受 GPU、显示器、系统负载及 DWM 影响。检查通过与静态截图不等于“目视已完全丝滑”；最终交互验收以及 T11/T12 的多应用、多窗口和硬件矩阵继续保留。

本机真实窗口样本为 2560×1368 工作区、150% DPI、渲染层级 2，管理一个自有 Probe，地图与工作区共 3 个缩略图关系。延后目录刷新后的单次记录：背景准备约 85ms，界面准备约 126ms（其中界面快照约 75ms、纹理宿主创建约 39ms）；动画回调间隔 P95 约 23ms、最大约 31ms，原生更新 P95 约 0.23ms。较早同类样本曾出现约 51ms 长间隔。该记录不代表可靠的对照实验，也不把回调间隔等同于实际显示帧率；剩余准备延迟和长帧明确保留为优化项。

Release 构建零警告/错误，217 模型、15 存储、51 原生检查通过。纯合成检查覆盖两种分辨率、时钟结束、停止与重复释放；真实自有 Probe 验证了 Tab / Shift+Tab 取消、地图和 CRT 内容、原点/空视窗/边缘裁剪三个工作区，以及独立恢复。未自动化的主观流畅度仍需用户验收。
