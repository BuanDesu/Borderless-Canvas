# 开发路线图

里程碑描述交付范围，不是工期承诺。只有完成退出条件才标记完成。

| 阶段 | 状态 | 交付 | 退出条件 |
| --- | --- | --- | --- |
| M0 基础项目 | 已完成 | 分层解决方案、模型预览、只读枚举、文档和模型检查 | 解决方案构建通过、模型检查通过、桌面壳离屏渲染通过 |
| M1 只读桌面映射 | T01–T03 已实现验证，PR 待合并 | 窗口准入规则、稳定身份、显示器工作区、事件监听、只读空间地图 | 自身/桌面/任务栏/其他虚拟桌面不被误纳入；创建关闭与拖动可跟踪 |
| M2 原生画布平移 | T04–T07 核心已实现，组合手势/应用及任务栏路径继续验收 | 恢复日志、单屏管理、全局导航、手势历史、原点与焦点 | 接管失败可恢复；同位移、尺寸不变；从任务栏激活离屏窗口可找回 |
| M3 空间总览与标签 | T08/T09 核心及自有窗口实测完成；T10 待推进 | 空间总览、搜索/方向选择、预览生命周期与标题降级；接续标签持久化 | 地图位置正确；Esc 不改变工作视角；目标关闭/预览失败不阻断导航 |
| M4 兼容与打包 | 未开始 | 多屏混合 DPI、拔插恢复、常用应用矩阵、性能分析、托盘与发布包 | 验收矩阵通过；无窗口遗失；记录版本和可复现性能数据 |

## 下一步任务

1. T10：动态标签 UI 与持久化；T08/T09 证据见[总览说明](spatial-overview.md)和[预览说明](overview-previews.md)。
2. 补齐 FR-02 运行中加入新窗口、优先当前视口附近空位且不移动既有世界布局；继续使用写前登记和失败恢复。
3. 延续 T07 的物理 Shift/双键组合与输入异常整体验收，当前证据见[导航说明](global-navigation.md)。
4. 延续 T06 的真实普通应用兼容验证及任务栏实际激活，当前证据见[管理说明](managed-canvas.md)。
5. 按[原型交付检查](prototype-plan.md)完成实际交互、恢复演练和试用产物。

不先增加应用内容缩放、虚拟桌面桥接、复杂插件系统、开机自启或联网更新。先确认真实窗口平移、空间地图和恢复组成的闭环。

## GitHub 任务索引

- M1：[T01 / #1](https://github.com/BuanDesu/Borderless-Flat-Window/issues/1)、[T02 / #2](https://github.com/BuanDesu/Borderless-Flat-Window/issues/2)、[T03 / #3](https://github.com/BuanDesu/Borderless-Flat-Window/issues/3)
- M2：[T04 / #4](https://github.com/BuanDesu/Borderless-Flat-Window/issues/4)、[T05 / #5](https://github.com/BuanDesu/Borderless-Flat-Window/issues/5)、[T06 / #6](https://github.com/BuanDesu/Borderless-Flat-Window/issues/6)、[T07 / #7](https://github.com/BuanDesu/Borderless-Flat-Window/issues/7)
- M3：[T08 / #8](https://github.com/BuanDesu/Borderless-Flat-Window/issues/8)、[T09 / #9](https://github.com/BuanDesu/Borderless-Flat-Window/issues/9)、[T10 / #10](https://github.com/BuanDesu/Borderless-Flat-Window/issues/10)
- M4：[T11 / #11](https://github.com/BuanDesu/Borderless-Flat-Window/issues/11)、[T12 / #12](https://github.com/BuanDesu/Borderless-Flat-Window/issues/12)、[T13 / #13](https://github.com/BuanDesu/Borderless-Flat-Window/issues/13)
