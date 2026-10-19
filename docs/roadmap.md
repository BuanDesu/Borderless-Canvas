# 开发路线图

里程碑描述交付范围，不是工期承诺。只有完成退出条件才标记完成。

| 阶段 | 状态 | 交付 | 退出条件 |
| --- | --- | --- | --- |
| M0 基础项目 | 已完成 | 分层解决方案、模型预览、只读枚举、文档和模型检查 | 解决方案构建通过、模型检查通过、桌面壳离屏渲染通过 |
| M1 只读桌面映射 | 进行中：T01/T02 已实现，T03 待推进 | 窗口准入规则、稳定身份、显示器工作区、事件监听、只读空间地图 | 自身/桌面/任务栏/其他虚拟桌面不被误纳入；创建关闭与拖动可跟踪 |
| M2 原生画布平移 | 未开始 | 恢复日志与恢复入口先落地，再做单屏窗口移动、平移手势、原点与焦点导航 | 接管失败可恢复；同位移、尺寸不变；从任务栏激活离屏窗口可找回 |
| M3 空间总览与标签 | 未开始 | 覆盖式总览、DWM 预览、方向选择/搜索、位置书签 UI 与持久化 | 地图位置正确；Esc 不改变工作视角；目标关闭/预览失败不阻断导航 |
| M4 兼容与打包 | 未开始 | 多屏混合 DPI、拔插恢复、常用应用矩阵、性能分析、托盘与发布包 | 验收矩阵通过；无窗口遗失；记录版本和可复现性能数据 |

## 下一步任务

1. T03：接入创建/关闭/位置/激活事件，将 destroy 失效通知接入身份表；使用 T02 坐标映射构建只读真实地图。
2. T04/T05：接管前先实现恢复日志与恢复入口，验证离屏方案。
3. T06：只接管几个可恢复的测试窗口，实现批量平移，再接入真实应用。
4. T08：把地图交互与工作相机分离，增加选择状态、取消与空间方向邻接。

不先增加应用内容缩放、虚拟桌面桥接、复杂插件系统、开机自启或联网更新。先确认真实窗口平移、空间地图和恢复组成的闭环。

## GitHub 任务索引

- M1：[T01 / #1](https://github.com/BuanDesu/Borderless-Flat-Window/issues/1)、[T02 / #2](https://github.com/BuanDesu/Borderless-Flat-Window/issues/2)、[T03 / #3](https://github.com/BuanDesu/Borderless-Flat-Window/issues/3)
- M2：[T04 / #4](https://github.com/BuanDesu/Borderless-Flat-Window/issues/4)、[T05 / #5](https://github.com/BuanDesu/Borderless-Flat-Window/issues/5)、[T06 / #6](https://github.com/BuanDesu/Borderless-Flat-Window/issues/6)、[T07 / #7](https://github.com/BuanDesu/Borderless-Flat-Window/issues/7)
- M3：[T08 / #8](https://github.com/BuanDesu/Borderless-Flat-Window/issues/8)、[T09 / #9](https://github.com/BuanDesu/Borderless-Flat-Window/issues/9)、[T10 / #10](https://github.com/BuanDesu/Borderless-Flat-Window/issues/10)
- M4：[T11 / #11](https://github.com/BuanDesu/Borderless-Flat-Window/issues/11)、[T12 / #12](https://github.com/BuanDesu/Borderless-Flat-Window/issues/12)、[T13 / #13](https://github.com/BuanDesu/Borderless-Flat-Window/issues/13)
