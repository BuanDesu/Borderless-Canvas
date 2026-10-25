# 功能 PR 集成记录

2026-10-19。按用户授权自行合并，保留 merge commit 和原功能提交。每项先核对对应 head 的 `check` 成功，按依赖顺序将目标分支调整到 main，再核对合并结果与该功能 head 的 Git tree 相同。

| PR | 功能 | 合并提交 |
| --- | --- | --- |
| [#14](https://github.com/BuanDesu/Borderless-Flat-Window/pull/14) | 窗口准入 | d2b7cf214b8f79b58a7b59268812ede3ccb4f7fc |
| [#15](https://github.com/BuanDesu/Borderless-Flat-Window/pull/15) | 显示器坐标 | c759c45152f355729a9ee9cdbea6e6b8e7d5953f |
| [#16](https://github.com/BuanDesu/Borderless-Flat-Window/pull/16) | 真实空间地图 | 9a57d47c6b53a1295f146bc16ee785e68611e212 |
| [#17](https://github.com/BuanDesu/Borderless-Flat-Window/pull/17) | 恢复日志 | bc5532c6efa44d5cb10802c018f7246c4f5296ae |
| [#18](https://github.com/BuanDesu/Borderless-Flat-Window/pull/18) | 离屏停放 | 83ff45a33c07904f041defaf39017729da34a7aa |
| [#19](https://github.com/BuanDesu/Borderless-Flat-Window/pull/19) | 原生画布 | b67f33841596f1d148a04c56b8bd4621211f8bfb |
| [#20](https://github.com/BuanDesu/Borderless-Flat-Window/pull/20) | 全局导航 | a1929e216f76daf6ac95e5fd9437f66cef2f3515 |
| [#21](https://github.com/BuanDesu/Borderless-Flat-Window/pull/21) | 空间总览 | 7e173915581b82959bfef1f3e3d668d2ae309e75 |
| [#22](https://github.com/BuanDesu/Borderless-Flat-Window/pull/22) | 窗口预览 | 5a01010e45806414d543447658173e4a7438557a |
| [#23](https://github.com/BuanDesu/Borderless-Flat-Window/pull/23) | 标签持久化 | 07fda5346d3c61199d8cd6a0b0f36e7b2a78f65d |
| [#25](https://github.com/BuanDesu/Borderless-Flat-Window/pull/25) | 运行中新窗接入 | 7379cd72ecd69bcd1629b8e383c044e674ccad52 |
| [#26](https://github.com/BuanDesu/Borderless-Flat-Window/pull/26) | 首个试用包 | 2e8818e5973d8be88534adae528030631f31c6d1 |

首个试用包代码集成后 tree：`2c1c06618e8079ce3437b4ba5d8915c209c75d29`。本地日志和桌面截图不纳入记录；表格只保存仓库内可验证的提交身份。

2026-10-25 触控板修正：[PR #69](https://github.com/BuanDesu/Borderless-Flat-Window/pull/69) 恢复接收层真实命中及正式画布双轴平移，合并提交 `89646c9f9900077231aa87f3ad3eb8cde4a7d23f`；[PR #70](https://github.com/BuanDesu/Borderless-Flat-Window/pull/70) 的透明期调整未通过桌面单次右键实体复验，保留其失败边界，合并提交 `a4a86a2aa9717f41e881668953655f8a28c5fbaf`。随后 [PR #71](https://github.com/BuanDesu/Borderless-Flat-Window/pull/71) 的桌面成对交接、退出状态顺序及匿名诊断在准确构建 `b02cd13` 上通过本机实体复验；完整来源、校验与自动检查见[交付记录](touch-context-handoff.md#实体复验与交付)。
