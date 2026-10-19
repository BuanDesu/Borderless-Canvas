# TODO

[GitHub 私有仓库](https://github.com/BuanDesu/Borderless-Flat-Window) · [全部 Issues](https://github.com/BuanDesu/Borderless-Flat-Window/issues) · [里程碑](https://github.com/BuanDesu/Borderless-Flat-Window/milestones)

当前状态：M0 已完成，M1 推进中；T01、T02 已实现并验证，待各自 PR 合并后勾选。T02 的负坐标与 100% DPI 基线由模型检查覆盖，本机为 150% DPI，只读实测范围见[坐标说明](docs/display-coordinates.md)。下一步为 T03 事件同步与真实地图。完成条件以[需求规格](windows-spatial-canvas-requirements.zh-CN.md)和[验收文档](docs/acceptance.md)为准。

## 已完成：M0 基础项目

- [x] C# / .NET 10 / WinForms 分层解决方案。
- [x] 世界坐标、原生比例相机和统一比例地图投影。
- [x] 相机平移、远处窗口聚焦、原点和导航历史模型。
- [x] 不限制固定槽位的位置书签模型。
- [x] 示例地图与 Windows 窗口候选只读枚举。
- [x] 构建脚本、7 项模型检查和桌面壳渲染检查。
- [x] 产品需求、架构、技术选型、路线图与验收规格。

## P0 / M1：建立可靠的只读桌面模型

- [ ] **T01：窗口身份与准入策略。** 用 HWND + PID + 进程启动时间绑定独立模型 ID；排除自身、桌面、任务栏、工具窗口、cloaked 窗口和其他虚拟桌面的窗口；输出明确排除原因。验证句柄复用、owner/模态关系和关闭竞态。对应 FR-02/09。 [Issue #1](https://github.com/BuanDesu/Borderless-Flat-Window/issues/1)
- [ ] **T02：显示器工作区与坐标边界。** 读取主屏/工作区/DPI，区分世界、视口与物理坐标；检查整数范围；先建立单屏 100% DPI 基线。验证负坐标显示器和任务栏工作区。对应 FR-01/07、NFR-04。依赖 T01。 [Issue #2](https://github.com/BuanDesu/Borderless-Flat-Window/issues/2)
- [ ] **T03：窗口生命周期与双向事件同步。** 跟踪创建、关闭、移动、尺寸和激活事件；区分用户拖动与自身移动回调；不使用持续全量轮询。验证快速开关窗口和事件乱序；输出只读真实空间地图。对应 FR-02/09。依赖 T01、T02。 [Issue #3](https://github.com/BuanDesu/Borderless-Flat-Window/issues/3)

## P0 / M2：安全接管与连续平移

- [ ] **T04：恢复日志和独立恢复入口。** 接管前落盘原始 placement、状态及身份；支持正常退出、强制结束后的重新启动恢复；跳过失效/复用 HWND，防止误移动新窗口。对应 FR-10、NFR-02。依赖 T01、T02。**必须先于真实窗口移动。** [Issue #4](https://github.com/BuanDesu/Borderless-Flat-Window/issues/4)
- [ ] **T05：离屏策略验证与决策记录。** 比较有界停放/裁剪等策略；验证窗口不漏到副屏、任务栏预览、弹窗和动态画面刷新；记录降级方式和可恢复边界。对应 FR-01/07、NFR-06。依赖 T02、T04。 [Issue #5](https://github.com/BuanDesu/Borderless-Flat-Window/issues/5)
- [ ] **T06：原生批量平移与焦点跟随。** 先接管测试窗口，再接入普通应用；平移不改尺寸与世界位置，不抢焦点；聚焦整体移动相机后请求激活；任务栏激活离屏目标时定位；局部失败可协调。对应 FR-01/03/04/07。依赖 T03、T04、T05。 [Issue #6](https://github.com/BuanDesu/Borderless-Flat-Window/issues/6)
- [ ] **T07：全局导航手势与模式状态机。** 配置快捷键、滚轮纵移、Shift 横移、左右双键拖动；解决误触/右键菜单/应用滚动竞争；Esc 释放捕获；一次手势合并为一条历史；快捷键冲突可见。对应 FR-04/08、NFR-03/07。依赖 T06。 [Issue #7](https://github.com/BuanDesu/Borderless-Flat-Window/issues/7)

## P0 / M3：空间总览与位置标签

- [ ] **T08：保留位置的总览、选择和搜索。** 独立于工作相机保存总览状态；按统一比例绘制真实位置、原点和视口；方向键按空间选择；搜索不重排；确认才移动工作相机，Esc 取消；目标关闭时安全处理。对应 FR-05。依赖 T03、T06。 [Issue #8](https://github.com/BuanDesu/Borderless-Flat-Window/issues/8)
- [ ] **T09：DWM 预览资源与降级。** 管理注册/更新/释放生命周期；不可预览或离屏不刷新时显示图标、标题或缓存占位；选择导航仍可用；不向缩略图转发编辑输入。对应 FR-05、NFR-06。依赖 T08。 [Issue #9](https://github.com/BuanDesu/Borderless-Flat-Window/issues/9)
- [ ] **T10：位置标签 UI 与本地持久化。** 新增、重命名、删除、搜索和跳转；稳定 ID、重名区分、无 1–9 槽位限制；版本化 JSON/原子写入/损坏降级；删除标签不移动窗口。对应 FR-06、NFR-05。依赖 T08。 [Issue #10](https://github.com/BuanDesu/Borderless-Flat-Window/issues/10)

## P1 / M4：兼容、性能与发布

- [ ] **T11：窗口特殊状态与多屏兼容。** 验证最小化、最大化、全屏、置顶、owner/模态框、输入法及权限不足；扩展混合 DPI、拔插/主屏变化、休眠和 Explorer 重启；记录常用应用兼容矩阵。对应 FR-12、AT-05/09/10/11/12。依赖 T06、T07、T09。 [Issue #11](https://github.com/BuanDesu/Borderless-Flat-Window/issues/11)
- [ ] **T12：性能基线与可撤销整理。** 测量 10/30/更多窗口、预览开关和离屏数量增长下的输入延迟、CPU/GPU/内存；合并高频更新；提供只影响选区的平铺与撤销，不自动压缩全画布。对应 FR-11、NFR-03。依赖 T07、T09。 [Issue #12](https://github.com/BuanDesu/Borderless-Flat-Window/issues/12)
- [ ] **T13：发布准备。** 托盘、暂停/恢复可见状态、配置入口；选择许可证，验证第三方许可；确定自包含部署/安装与卸载方式；检查启动失败与恢复指引；以完整桌面验收矩阵决定是否发布。依赖 T10、T11、T12。 [Issue #13](https://github.com/BuanDesu/Borderless-Flat-Window/issues/13)

## 维护规则

- GitHub Issues 用于记录具体实现、讨论和验证证据；本文件保持阶段性总览，并回填对应 Issue 链接。
- 创建任务时写清优先级、依赖、需求编号和可检查的完成条件；前置条件未满足时不提前勾选。
- M0 示例能力不等于真实桌面能力。阶段推进后同步更新 README、路线图和验收记录。
- 应用内容缩放、虚拟桌面桥接、云同步和插件市场不在当前待办范围内。
