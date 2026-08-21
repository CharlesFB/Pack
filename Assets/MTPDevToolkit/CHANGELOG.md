# Changelog
### [1.2.8] - 2026-04-23

#### 修改
- [BUG] 修改下载插件时候报错

### [1.2.7] - 2025-05-20

#### 新增
- [Add] 编辑配置信息时, 支持编辑ChangeLog.
- [Add] 提供两种方式支持自动填充ChangeLog, 一种方式是使用历史Git提交信息作为ChangeLog, 另一种是利用AI自动生成的ChangeLog
#### 修改
- [Mod] 在package发布前检查是否有未推送的提交, 如果存在将阻止发布
- [Mod] CHANGELOG.md随package.json一同提交
