# TODO

[GitHub 私有仓库](https://github.com/BuanDesu/Borderless-Flat-Window) · [全部 Issues](https://github.com/BuanDesu/Borderless-Flat-Window/issues) · [里程碑](https://github.com/BuanDesu/Borderless-Flat-Window/milestones)

当前状态：首个原型的 T01–T10 与 FR-02 功能 PR 已按授权合并，[提交记录](docs/integration-history.md)可追踪。新版加入自动导航、工作区总览、同 DPI 多屏与托盘；验证范围和待人工项目见[反馈改进](docs/interaction-refresh.md)。T11–T13 的完整兼容、性能与发布条件尚未完成。

## 已完成：M0 基础项目

- [x] C# / .NET 10 / WinForms 分层解决方案。
- [x] 世界坐标、原生比例相机和统一比例地图投影。
- [x] 相机平移、远处窗口聚焦、原点和导航历史模型。
- [x] 不限制固定槽位的位置书签模型。
- [x] 示例地图与 Windows 窗口候选只读枚举。
- [x] 构建脚本、7 项模型检查和桌面壳渲染检查。
- [x] 产品需求、架构、技术选型、路线图与验收规格。

## P0 / M1：建立可靠的只读桌面模型

- [x] **T01：窗口身份与准入策略。** 用 HWND + PID + 进程启动时间绑定独立模型 ID；排除自身、桌面、任务栏、工具窗口、cloaked 窗口和其他虚拟桌面的窗口；输出明确排除原因。验证句柄复用、owner/模态关系和关闭竞态。对应 FR-02/09。 [Issue #1](https://github.com/BuanDesu/Borderless-Flat-Window/issues/1)
- [x] **T02：显示器工作区与坐标边界。** 读取主屏/工作区/DPI，区分世界、视口与物理坐标；检查整数范围；先建立单屏 100% DPI 基线。验证负坐标显示器和任务栏工作区。对应 FR-01/07、NFR-04。依赖 T01。 [Issue #2](https://github.com/BuanDesu/Borderless-Flat-Window/issues/2)
- [x] **T03：窗口生命周期与双向事件同步。** 跟踪创建、关闭、移动、尺寸和激活事件；区分用户拖动与自身移动回调；不使用持续全量轮询。验证快速开关窗口和事件乱序；输出只读真实空间地图。对应 FR-02/09。依赖 T01、T02。 [Issue #3](https://github.com/BuanDesu/Borderless-Flat-Window/issues/3)

## P0 / M2：安全接管与连续平移

- [x] **T04：恢复日志和独立恢复入口。** 接管前落盘原始 placement、状态及身份；支持正常退出、强制结束后的重新启动恢复；跳过失效/复用 HWND，防止误移动新窗口。对应 FR-10、NFR-02。依赖 T01、T02。**必须先于真实窗口移动。** [Issue #4](https://github.com/BuanDesu/Borderless-Flat-Window/issues/4)
- [x] **T05：离屏策略验证与决策记录。** 比较有界停放/裁剪等策略；验证窗口不漏到副屏、任务栏预览、弹窗和动态画面刷新；记录降级方式和可恢复边界。对应 FR-01/07、NFR-06。依赖 T02、T04。 [Issue #5](https://github.com/BuanDesu/Borderless-Flat-Window/issues/5)
- [x] **T06：原生批量平移与焦点跟随。** 先接管测试窗口，再接入普通应用；平移不改尺寸与世界位置，不抢焦点；聚焦整体移动相机后请求激活；任务栏激活离屏目标时定位；局部失败可协调。对应 FR-01/03/04/07。依赖 T03、T04、T05。 [Issue #6](https://github.com/BuanDesu/Borderless-Flat-Window/issues/6)
- [x] **T07：全局导航手势与模式状态机。** 配置快捷键、滚轮纵移、Shift 横移、左右双键拖动；解决误触/右键菜单/应用滚动竞争；Esc 释放捕获；一次手势合并为一条历史；快捷键冲突可见。对应 FR-04/08、NFR-03/07。依赖 T06。 [Issue #7](https://github.com/BuanDesu/Borderless-Flat-Window/issues/7)

## P0 / M3：空间总览与位置标签

- [x] **T08：保留位置的总览、选择和搜索。** 独立于工作相机保存总览状态；按统一比例绘制真实位置、原点和视口；方向键按空间选择；搜索不重排；确认才移动工作相机，Esc 取消；目标关闭时安全处理。对应 FR-05。依赖 T03、T06。 [Issue #8](https://github.com/BuanDesu/Borderless-Flat-Window/issues/8)
- [x] **T09：DWM 预览资源与降级。** 管理注册/更新/释放生命周期；不可预览或离屏不刷新时显示图标、标题或缓存占位；选择导航仍可用；不向缩略图转发编辑输入。对应 FR-05、NFR-06。依赖 T08。 [Issue #9](https://github.com/BuanDesu/Borderless-Flat-Window/issues/9)
- [x] **T10：位置标签 UI 与本地持久化。** 新增、重命名、删除、搜索和跳转；稳定 ID、重名区分、无 1–9 槽位限制；版本化 JSON/原子写入/损坏降级；删除标签不移动窗口。对应 FR-06、NFR-05。依赖 T08。 [Issue #10](https://github.com/BuanDesu/Borderless-Flat-Window/issues/10)

## P0 / 原型集成补齐

- [x] **FR-02 补齐：运行中新窗口接入。** 手动选择与默认开启、可关闭的自动接入；排除原有未选窗口，稳定后寻找视口附近空位；保留旧布局/相机/标签，仅登记并移动新增项。已完成模型、文件、原生和五窗实际接入/恢复验证，功能 PR 已合并。依赖 T03、T04、T06、T10。 [Issue #24](https://github.com/BuanDesu/Borderless-Flat-Window/issues/24)

## P1 / M4：兼容、性能与发布

本轮反馈 [Issue #27](https://github.com/BuanDesu/Borderless-Flat-Window/issues/27)：

- [x] 桌面自动导航，应用内输入透传；桌面 Tab 总览、Ctrl＋滚轮轮选。
- [x] Y2K 主题，三页入口与三个诊断子页；工作区并入毛玻璃总览。
- [x] 同 DPI 多屏工作区并集、主屏聚焦、离屏停放和恢复模型。
- [x] 新窗默认自动接入，托盘隐藏/返回/暂停/退出。
- [x] 最终包上的物理组合、工作区编辑、托盘入口及真实双屏人工验收（用户确认全部正常）。

视觉改进 [Issue #29](https://github.com/BuanDesu/Borderless-Flat-Window/issues/29)：糖果色玻璃材质、悬浮按钮、精简主命令栏、二级菜单和任务视图式总览，见[设计记录](docs/candy-glass-design.md)。

本轮继续改进 [Issue #31](https://github.com/BuanDesu/Borderless-Flat-Window/issues/31)：

- [x] 原创八尺寸 ICO，用于 EXE、标题栏及托盘，验证图标资源。
- [x] 播放器渐变外壳、像素胶囊/爱心/糖果、圆角内容屏幕。
- [x] 总览统一通透模糊背景，搜索并入底部，CRT 工作区展示对应视角内的窗口位置。
- [x] 单独右击与双键手势仲裁，菜单期间暂停自动导航；补充四组模型回归。
- [x] 202 模型、15 文件、48 原生检查与常规/紧凑布局复核。
- [x] 用户已确认：桌面单独右击及菜单关闭后滚轮正常，双键松开不误弹菜单、Backspace 返回起点，原创托盘图标可见且双击返回。

新反馈 [Issue #33](https://github.com/BuanDesu/Borderless-Flat-Window/issues/33)，规则见[面板、过渡与取色](docs/console-and-wallpaper.md)：

- [x] 独立总览宿主、单缓冲场景过渡，当前/最后焦点窗口 DWM 缩小归位及禁用动画降级。
- [x] 标签与工作区名称按实际字体测量；诊断说明按宽度换行。
- [x] 右下角竖向游戏机面板，底部操作、外部点击收起与托盘返回。
- [x] 本地壁纸 25 点取色、糖果色匹配、后台采样、系统事件刷新与失败回退。
- [x] 用户已确认面板位置、自动收起和托盘点击返回；继续优化过渡与外观。

新反馈 [Issue #35](https://github.com/BuanDesu/Borderless-Flat-Window/issues/35)，规则与边界见[总览合成与像素按键](docs/overview-compositor.md)：

- [x] Tab / Shift+Tab 往返总览，Ctrl+Tab 轮选，保留模态编辑的 Tab 遍历。
- [x] 完全自绘圆角按钮与 SVG 键帽；私有加载简中/英文像素字体并随包保留许可证。
- [x] 地图取消默认 16 项和离屏/重叠过滤，CRT 工作区使用真实窗口内容、视窗裁剪和多屏分区。
- [x] 预热并复用预览；缓存覆盖层过渡，不反复显示/隐藏子窗口；无变化不更新原生位置。
- [x] 217 模型、15 存储、51 原生检查及真实自有 Source 的地图/工作区预览和 Tab 退出。
- [x] 用户确认：无方形黑框，字体/圆角/标签完整，不同视角含空视窗和边缘裁剪，离屏地图仍有预览。
- [ ] 用户反馈过渡仍卡顿、界面质感不足，后续由 #37 继续验收。

新反馈 [Issue #37](https://github.com/BuanDesu/Borderless-Flat-Window/issues/37)，参考与规则见 [Fluent 控制台与合成层过渡](docs/fluent-motion.md)：

- [x] 重读 vxwm 无限画布、聚焦、回原点与缩放模块，区分 X11 dwm 和 Windows DWM。
- [x] Fluent 间距与层级、同尺寸同基线的主按键、等宽分组；系统正文字体与像素品牌字分层。
- [x] WPF 纹理合成替代逐帧整屏 CPU 混合；预热、逐帧 DWM 更新、透明度终值与降级路径。
- [x] 壁纸内存小图复用、主题/显示器变化失效；纯合成基线与可选匿名耗时记录。
- [x] 283 项模型/存储/原生检查；两种分辨率合成时钟与释放、真实自有 Probe 往返和工作区内容复核。CI、合并和试用包校验记录回填 #37。
- [x] 用户反馈：面板方向大体合适；过渡仍卡顿、Tab 不能快速往返，关闭键与留白问题转入 #39。

新反馈 [Issue #39](https://github.com/BuanDesu/Borderless-Flat-Window/issues/39)，实现与边界见[可中断总览与页面过渡](docs/responsive-transitions.md)：

- [x] 可中断、可反向的总览时钟；准备期间和过渡中继续处理切换请求，保留模态输入。
- [x] 缓存返回场景，合成层焦点图像缩放及保守遮挡降级，子控件仅复制对应背景区域。
- [x] 48×48 居中关闭键、外壳圆角与文字区分离、DPI 留白。
- [x] 主面板与诊断子页淡入，快速改选保留最后目标，关闭动画时直接切换。
- [x] 用户确认主面板、关闭键和文字区域目前没有太大问题。
- [ ] 用户复验：物理快速连按 Tab / Shift+Tab 和实际桌面流畅度，继续在 #41 跟踪。
- [ ] T11/T12 继续追踪完整回归中偶发的文件替换访问失败与夹具初始化问题，不以单次成功关闭。

后续反馈 [Issue #41](https://github.com/BuanDesu/Borderless-Flat-Window/issues/41)，详见[总览渲染重构](docs/retained-overview.md)：

- [x] 用户确认主面板通过，选择保留空间关系并重做总览渲染。
- [x] 核对原生任务视图的公开接口边界；不以系统选窗列表代替空间地图。
- [x] 单个保留式场景承载地图、搜索和工作区；运行时移除旧控件树、整页截图与整屏 CPU 模糊。
- [x] 输入会话编号隔离晚到关闭/显示通知，实际激活后结束准备期；确认后立即交接。
- [x] 生产窗体生命周期与三种尺寸的合成场景检查；生产图像禁止导出。
- [x] 统一使用 WPF 当前动画值推进预览、反向和完成；80ms 界面停顿回归先复现再修复，已处理的准备取消不会先显示再关闭。
- [x] 用户复验后修正选窗后的 Tab 焦点回落：可交互总览表面提供返回兜底，菜单/模态/修饰键保持原生，准备期不截取切走后的应用输入。
- [x] 用户确认资源管理器选窗后再次进入总览，Tab 已不会落入资源管理器控件。
- [x] 空闲时有界准备一个隐藏宿主；完整输入失效、单次转移和重入代号保护，按键前成本单列。修正隐藏句柄的 TopMost 激活，保留前台不变断言及真实源回归。
- [x] 焦点截图改为读回前后分别核验，覆盖遮挡移除竞态和失败释放；拆分读回/核验/上传时序，并以匿名样式分类解释遮挡降级。
- [x] [直接读回实验](docs/capture-readback.md)完成合成原生颜色/资源基准与真实样本核对；真实收益证据不足，保留开发者显式启用，默认不切换。
- [x] [原生总览宿主实验](docs/live-overview-surface.md)：不读取桌面像素的系统背景请求与实时缩略图矩形过渡，正反向、端点空闲及中途关闭独立验证；背景视觉和完整生产接入尚未确认，保持显式启用。
- [x] [完整总览的原生适配](docs/native-overview-presenter.md)：共享视图、真实宿主接口、地图/搜索/工作区及全局会话接入；三尺寸、双背景模式、空闲后反向、模态输入与计时回归。提供显式试用入口，背景与自然操作验收仍未完成，默认不切换。
- [x] [原生背景修正](docs/native-backdrop-validation.md)：显式启用并读取非客户区渲染策略；十组自有图案对照验证实际背景响应与细条纹衰减，遮挡/位置变化控制零读回。完整原生总览已观察到模糊背景，选窗、重开、Tab 返回与暂停恢复通过，默认仍不切换。
- [ ] 最终版本的真实选窗后重开、快速物理 Tab 和流畅度验收；T11/T12 继续性能与兼容工作。

T12 已补充[总览负载基线](docs/overview-load-baseline.md)：48 个自有原生窗口样本、预览开关及离屏增长，拆分入口计时；同步作用域复用 COM 查询服务，保留全部身份和桌面核验。300 项检查通过。真实输入/呈现、GPU/长期资源矩阵和区域整理仍待完成，T12 不据此勾选。

- [ ] **T11：窗口特殊状态与多屏兼容。** 验证最小化、最大化、全屏、置顶、owner/模态框、输入法及权限不足；扩展混合 DPI、拔插/主屏变化、休眠和 Explorer 重启；记录常用应用兼容矩阵。对应 FR-12、AT-05/09/10/11/12。依赖 T06、T07、T09。 [Issue #11](https://github.com/BuanDesu/Borderless-Flat-Window/issues/11)
- [ ] **T12：性能基线与可撤销整理。** 测量 10/30/更多窗口、预览开关和离屏数量增长下的输入延迟、CPU/GPU/内存；合并高频更新；提供只影响选区的平铺与撤销，不自动压缩全画布。对应 FR-11、NFR-03。依赖 T07、T09。 [Issue #12](https://github.com/BuanDesu/Borderless-Flat-Window/issues/12)
- [ ] **T13：发布准备。** 托盘、暂停/恢复可见状态、配置入口；选择许可证，验证第三方许可；确定自包含部署/安装与卸载方式；检查启动失败与恢复指引；以完整桌面验收矩阵决定是否发布。依赖 T10、T11、T12。 [Issue #13](https://github.com/BuanDesu/Borderless-Flat-Window/issues/13)

## 维护规则

- GitHub Issues 用于记录具体实现、讨论和验证证据；本文件保持阶段性总览，并回填对应 Issue 链接。
- 创建任务时写清优先级、依赖、需求编号和可检查的完成条件；前置条件未满足时不提前勾选。
- M0 示例能力不等于真实桌面能力。阶段推进后同步更新 README、路线图和验收记录。
- 应用内容缩放、虚拟桌面桥接、云同步和插件市场不在当前待办范围内。
