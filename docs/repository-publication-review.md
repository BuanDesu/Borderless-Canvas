# 仓库公开前复核

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
| 项目许可 | 尚未指定项目源码的公开许可证；第三方原文与项目授权分开保留，继续由 #13 跟踪 |

GitHub [提交邮箱说明](https://docs.github.com/en/account-and-profile/how-tos/email-preferences/setting-your-commit-email-address)明确，设置变更不修改既有提交邮箱。官方[历史数据清理说明](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)指出旧 PR 引用和缓存需要平台协助，是否受理由 GitHub 决定。因此本次不将账户展示设置视作清理完成的证据。

## 保留的真实待办

- [#11](https://github.com/BuanDesu/Borderless-Flat-Window/issues/11)：特殊窗口状态、混合 DPI、真实硬件切换及应用兼容矩阵。
- [#12](https://github.com/BuanDesu/Borderless-Flat-Window/issues/12)：真实输入 P95、呈现与长期资源测量。已有自有窗口负载样本和整理撤销实现。
- [#13](https://github.com/BuanDesu/Borderless-Flat-Window/issues/13)：项目许可、分发审查、干净系统验收及正式版交付条件。
- [#72](https://github.com/BuanDesu/Borderless-Flat-Window/issues/72)：旧 PR 引用、旧提交和平台缓存的作者身份清理。

前三项保留为开发与正式版验收工作，源码仓库公开后也继续跟踪；不把它们误记为已经通过。当前公开操作等待 #72 的平台处理与复核，不创建正式版发布记录。

## 执行顺序

1. 向 GitHub 官方支持提供仓库、38 个受影响 PR、首次改变的提交映射及已完成的匿名化信息；原始邮箱、运行报告和备份继续留在本机。
2. 记录实际工单与平台答复。当前申请草稿已准备，官方支持门户需要所有者登录，尚未提交；提交后更新 #72。
3. 平台处理后重新检查旧 SHA、受影响 PR 引用和可达历史；平台拒绝或仍可读取时，保留私有并与所有者确定下一步。
4. 通过复核后按所有者授权公开仓库，同步当前说明及维护规则，并验证匿名访问可见性。

备份、旧新提交映射、输入报告、设置、恢复日志、真实桌面画面和试用产物保留在忽略目录。用户关闭邮箱展示的操作不要求提供账户截图或邮箱原文。
