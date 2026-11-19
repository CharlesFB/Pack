# 第四步：恢复与执行边界验收

更新：2026-09-15。实现范围为独立单条 UPM 安装；下一步是 unitypackage 导入。

## 已实现

- 测试统一放入 `dashboard/tests/`，默认入口运行完整 87 项测试，包含 migration 0006 → 0007 的历史任务保留验证。
- 每个步骤允许多次 Attempt。明确失败后，用户填写修复/重试原因才建立新尝试，保存请求 UUID、操作账号（本地匿名模式为空）、前次尝试及审计时间。重复请求幂等，旧尝试回执不能覆盖新尝试。
- 只允许预检未安装、或 Add 明确结束失败且目标未满足的任务重试。其他未知结果保留工程锁，只提供重新核对；超时不证明操作失败。
- 网页显示执行器故障、尝试次数、最近十次尝试及重试原因；未就绪时不能新建任务。
- 执行器版本 2 才领取新的未派发任务，旧版本仍可回传已有任务。
- 启动后先对齐服务端任务。调用 Add 前持久化独立意图文件 `intents/<attempt-id>`；旧日志、缺失日志、旧协议日志均不会盲目重装。
- 日志损坏或文件写入失败时停止本地操作，继续向服务发送诊断。不会上传尚未持久化的内存进度，不自动回退到旧备份。

## 实机验收

使用 Unity 2022.3.62f2、临时 SQLite、独立服务和 `.runtime/` 下的隔离工程，不改业务工程依赖。下列路径均相对于 ServiceBase，目录内 `result.json` 为结果，另保留服务和 Unity 日志。

- Registry 安装、域重载 1 → 2、丢终态确认后恢复：`.runtime/upm-smoke-1789465663/`。
- HTTPS Git Basic 认证、完整 commit 和 `?path=/Unity/Fixture`：`.runtime/upm-recovery-git-e8e9c1cf/`。实际发生认证挑战与 Git 下载；Add 一次。
- 服务进程退出并重启：`.runtime/upm-recovery-restart-23dd7b43/`，恢复成功，Add 一次。
- Unity 进程 SIGKILL 后重启：`.runtime/upm-recovery-crash-43bb6d6d/`，恢复成功，Add 一次。
- 恢复较旧的 accepted 日志，同时保留独立意图标记：`.runtime/upm-recovery-rollback-journal-2d7b4acc/`，恢复成功，Add 仍只有一次。
- 服务返回 503 后恢复：`.runtime/upm-recovery-outage-275e3ec9/`，终态重新同步成功。
- 日志损坏：`.runtime/upm-recovery-corrupt-de37279b/`，显示诊断并保留锁。
- 日志目录不可写：`.runtime/upm-recovery-write-failure-ed62a5f8/`，显示诊断并保留锁，Add 零次。
- 首次派发响应丢失，随后重启并进入超时：`.runtime/upm-recovery-lost-delivery-ee5de748/`，只核对、进入 needs_review，Add 零次，拒绝不安全重试。
- 明确预检失败后记录原因并重试，重复提交同一请求：`.runtime/upm-recovery-retry-2f5ad258/`，只创建第二次尝试，安装成功，Add 一次。
- MTP 恢复记录与程序集身份阻止：`.runtime/upm-recovery-mtp-record-b67ac8ac/`、`.runtime/upm-recovery-mtp-loaded-42c1065c/`，拒绝新任务，Add 零次。

复现命令：

```sh
.venv/bin/python manage.py test dashboard
.venv/bin/python tests/run_upm_smoke.py
.venv/bin/python tests/run_upm_recovery_smoke.py --scenario git
```

恢复脚本还支持 `restart`、`crash`、`outage`、`corrupt`、`write-failure`、`lost-delivery`、`mtp-record`、`mtp-loaded`、`retry` 和 `rollback-journal`。每次创建独立工程并在退出时关闭自己的服务和编辑器。

## MTP 边界和未覆盖项

真实 MTP 源码仍有编辑器焦点、UPM 包变化等全局监听，因此独立 asmdef 不能保证其业务不被触发。目前检查包名 `com.magictavern.mtpmanager`、程序集 `Magictavern.FrontEngine.MTPManager`，以及 Library 内的 `mtp-install-recovery.json`、`mtp-install-progress.json`、`mtp-pending-manifest.json`；命中即阻止新安装，不调用 MTP、不清理其记录。浏览与只读快照继续按原适配器工作。

MTP 测试使用恢复文件及最小程序集身份夹具，结合真实源码静态检查；没有执行真实 MTP 业务安装，不代表严格共存通过。其他历史/改名 MTP 标识也需单独确认。

Git 验收使用本地真实 HTTPS Git 服务和临时凭据，证书仅在测试进程中信任，没有关闭 TLS 校验；公司 GitLab 的凭据、代理和网络仍需部署环境验证。连接故障通过 503 注入，崩溃使用 SIGKILL；部分超时场景向前调整测试日志时间，未等待真实十分钟。没有覆盖整机断电、所有文件系统及其他 Unity 版本。意图文件与日志并非跨服务器和客户端的原子事务，无法证明结果时保持阻塞。

## 升级与故障处理

服务端运行 `manage.py migrate`（新增 0007），整体更新 Unity 的 `UpmExecutor/` 目录及已有 Bridge 的 `BridgePullClient.cs`。升级前后的不确定任务保持核对模式，不自动补发 Add。

日志位于工程 `.servicebase/upm/`，包含 active、history 和 intents。保留目录，勿随 Library 清理，也不要通过删日志或意图文件重试。日志可能包含工程原始 manifest 的本地凭据，不上传服务。

文件故障先修复权限或恢复匹配该工程、服务器、尝试的原始日志，再使用 **Tools → ServiceBase → UPM → Reconnect and Recheck**。未知状态使用网页“重新核对”；只有明确失败并修复原因后才使用“记录原因并重试”。未提供强制解锁入口。
