# 开发路线图

里程碑描述交付范围，不是工期承诺。只有完成退出条件才标记完成。

| 阶段 | 状态 | 交付 | 退出条件 |
| --- | --- | --- | --- |
| M0 基础项目 | 已完成 | 分层解决方案、模型预览、只读枚举、文档和模型检查 | 解决方案构建通过、模型检查通过、桌面壳离屏渲染通过 |
| M1 只读桌面映射 | T01–T03 已实现验证，PR 已合并 | 窗口准入规则、稳定身份、显示器工作区、事件监听、只读空间地图 | 自身/桌面/任务栏/其他虚拟桌面不被误纳入；创建关闭与拖动可跟踪 |
| M2 原生画布平移 | 单屏原型及用户物理手势/任务栏路径已验证，PR 已合并 | 恢复日志、单屏管理、全局导航、手势历史、原点与焦点 | 接管失败可恢复；同位移、尺寸不变；从任务栏激活离屏窗口可找回 |
| M3 空间总览与标签 | T08–T10 核心和自有窗口/重启实测完成，PR 已合并 | 空间总览、搜索/方向选择、预览生命周期与标题降级、动态标签持久化 | 地图位置正确；Esc 不改变工作视角；目标关闭/预览失败不阻断导航；标签重启保留 |
| M4 兼容与打包 | 本地原型已交付；完整矩阵和正式发布尚未完成 | FR-02 新窗接入、常用应用与任务栏验收、试用产物；同 DPI 多屏与托盘已实现，仍需完整兼容、性能及正式发布验收 | 验收矩阵通过；无窗口遗失；记录版本和可复现性能数据 |

最新反馈改进见[自动导航、多屏与工作区](interaction-refresh.md)，历史功能合并见[集成记录](integration-history.md)。

## 下一步任务

1. M3 证据见[总览说明](spatial-overview.md)、[预览说明](overview-previews.md)和[标签说明](location-bookmarks.md)；进入可试用原型集成验收。
2. FR-02 已实现运行中加入新窗、视口附近空位和原布局保留，五窗主 GUI 强杀/重启/独立恢复已通过；见[接入说明](runtime-window-admission.md)。
3. 用户已补齐 Shift/双键、整段返回和任务栏找回证据；后续继续输入异常压力和完整快捷键配置，见[导航说明](global-navigation.md)。
4. 系统计算器、独立记事本及混合四窗已验证；继续更广应用、多屏和权限兼容。
5. 本地包、指南、校验、恢复演练及原型检查已完成，见[完成审计](prototype-audit.md)；正式发布继续按 T11–T13 推进。

不先增加应用内容缩放、虚拟桌面桥接、复杂插件系统、开机自启或联网更新。先确认真实窗口平移、空间地图和恢复组成的闭环。

## GitHub 任务索引

- M1：[T01 / #1](https://github.com/BuanDesu/Borderless-Flat-Window/issues/1)、[T02 / #2](https://github.com/BuanDesu/Borderless-Flat-Window/issues/2)、[T03 / #3](https://github.com/BuanDesu/Borderless-Flat-Window/issues/3)
- M2：[T04 / #4](https://github.com/BuanDesu/Borderless-Flat-Window/issues/4)、[T05 / #5](https://github.com/BuanDesu/Borderless-Flat-Window/issues/5)、[T06 / #6](https://github.com/BuanDesu/Borderless-Flat-Window/issues/6)、[T07 / #7](https://github.com/BuanDesu/Borderless-Flat-Window/issues/7)
- M3：[T08 / #8](https://github.com/BuanDesu/Borderless-Flat-Window/issues/8)、[T09 / #9](https://github.com/BuanDesu/Borderless-Flat-Window/issues/9)、[T10 / #10](https://github.com/BuanDesu/Borderless-Flat-Window/issues/10)
- 原型集成：[FR-02 / #24](https://github.com/BuanDesu/Borderless-Flat-Window/issues/24)
- M4：[T11 / #11](https://github.com/BuanDesu/Borderless-Flat-Window/issues/11)、[T12 / #12](https://github.com/BuanDesu/Borderless-Flat-Window/issues/12)、[T13 / #13](https://github.com/BuanDesu/Borderless-Flat-Window/issues/13)
