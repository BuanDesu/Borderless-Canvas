# 源码迁移与公开复核

## 2026-10-28：Borderless Canvas 公开源码

所有者授权将独立迁移的新仓库命名为 [Borderless Canvas](https://github.com/BuanDesu/Borderless-Canvas)，补充项目介绍并公开源码。新仓库只接收经过独立验收的代码分支和标签，没有复制旧 Issues、PR、Actions 日志、Release 或发行资产。当前运行与构建入口见 [README](../README.md)；本仓库尚无可下载的 Release。

迁移基线含 1 个分支、4 个附注标签、135 个提交、60 个合并提交和 2,087 个可达对象。完整可达历史、文件和路径中的已识别个人邮箱及检查的编码变体为零命中；新远端独立获取、元数据核验、隐私守卫、git fsck 和基线 CI 通过。基线过滤未改变提交/标签哈希、代码树或合并拓扑。公开准备仅修正文档、当前入口和维护规则，并禁用固定指向旧仓库的旧发布入口，不创建发行版，也不改写已推送历史。

旧私有仓库保持原状；它的旧 PR 引用、平台缓存和外部副本未因迁移而删除，旧仓库 #72 的问题仍须单独处理。新仓库源码公开不表示旧平台副本已清除，也不表示 T11–T13 的兼容、性能或正式版分发条件已完成。

## 旧私有仓库复核记录（历史）

以下为迁移前旧私有仓库的带日期记录，包含当时的可见性、套餐限制、工单和发布顺序；其中“当前”“本次”指各条记录的当时状态，不是新仓库的维护要求。旧工单链接未复制，访客可能无法访问。

2026-10-26，核验基线 `40a02b66fbee2ae2ba6f29ac21f35fadcfb947b6`。所有者已提出公开仓库，并在复核历史邮箱后明确选择：先由 GitHub 清理旧引用及平台副本，通过复核后再公开。当前保持私有，平台处理由 [#72](https://github.com/BuanDesu/Borderless-Flat-Window/issues/72) 跟踪。

## 本轮结果

| 项目 | 结果 |
| --- | --- |
| 远程分支 | 仅 main，已开启合并后自动删分支 |
| Pull request | 没有未合并 PR |
| Actions | 65 条现存运行全部成功，基线精确 head 的 CI 成功 |
| 可达项目历史 | 115 个提交均为 GitHub 隐私邮箱；完整身份守卫通过 |
| 内容复扫 | Gitleaks 对普通项目分支的全部可达变更复扫通过，未检出凭据；原始报告仅本地保存 |
| 平台旧对象 | 仍可通过 API 读取重写前的旧作者邮箱；关闭个人资料邮箱展示没有改变既有提交 |
| 项目许可 | 已按所有者决定采用根目录 MIT 许可证；第三方原文与项目授权分开保留，完整分发审查继续由 #13 跟踪 |

GitHub [提交邮箱说明](https://docs.github.com/en/account-and-profile/how-tos/email-preferences/setting-your-commit-email-address)明确，设置变更不修改既有提交邮箱。官方[历史数据清理说明](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)指出旧 PR 引用和缓存需要平台协助，是否受理由 GitHub 决定。因此本次不将账户展示设置视作清理完成的证据。

## 保留的真实待办

- [#11](https://github.com/BuanDesu/Borderless-Flat-Window/issues/11)：特殊窗口状态、混合 DPI、真实硬件切换及应用兼容矩阵。
- [#12](https://github.com/BuanDesu/Borderless-Flat-Window/issues/12)：真实输入 P95、呈现与长期资源测量。已有自有窗口负载样本和整理撤销实现。
- [#13](https://github.com/BuanDesu/Borderless-Flat-Window/issues/13)：项目 MIT 许可已选定；分发审查、干净系统验收及正式版交付条件仍开放。
- [#72](https://github.com/BuanDesu/Borderless-Flat-Window/issues/72)：旧 PR 引用、旧提交和平台缓存的作者身份清理。

前三项保留为开发与正式版验收工作，源码仓库公开后也继续跟踪；不把它们误记为已经通过。当前公开操作等待 #72 的平台处理与复核，不创建正式版发布记录。

## 执行顺序

1. 向 GitHub 官方支持提供仓库、38 个受影响 PR、首次改变的提交映射及已完成的匿名化信息；原始邮箱、运行报告和备份继续留在本机。
2. 记录实际工单与平台答复。所有者完成官方支持门户登录并确认提交后，申请已于 2026-10-26 成功创建；已核验工单正文与 Open 状态，无附件，工单标识与截图仅本地保存。当前等待平台处理，进展更新 #72。
3. 平台处理后重新检查旧 SHA、受影响 PR 引用和可达历史；平台拒绝或仍可读取时，保留私有并与所有者确定下一步。
4. 通过复核后按所有者授权公开仓库，同步当前说明及维护规则，并验证匿名访问可见性。

备份、旧新提交映射、输入报告、设置、恢复日志、真实桌面画面和试用产物保留在忽略目录。用户关闭邮箱展示的操作不要求提供账户截图或邮箱原文。

## 2026-10-27 维护复核：main 保护限制

在 `7990cc6` 基线上只读复核：仓库为 private，当前访问权限为 admin；branches API 返回 main 的 `protected:false`。branch protection 与 rulesets API 均明确返回 HTTP 403，提示当前私有仓库套餐不支持该功能。这是平台强制保护的独立 P2，未因部署文档修复而解决，也不能表述为已配置必需检查。

当前继续按 `AGENTS.md` 执行精确 PR head、两项 CI（`check` 与 `Hidden UI and scene checks`）成功、仓库私有状态、noreply 元数据及推送前完整可达祖先隐私检查；这些流程约束不等于平台强制规则。是否购买支持该功能的套餐，以及届时如何要求 CI、禁止 force push/删除与约束直接推送，待所有者决定并核对对现有合并流程的影响。本次没有改变套餐、计费、保护设置或仓库可见性。旧平台隐私副本仍由 #72 跟踪。
