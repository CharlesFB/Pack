# NativeUtility 的 UPM Git 安装失败：定位与修复

## 2026-09-22 结论

TestService / Unity 6000.3.19f1 / Git 2.54.0（Apple Git-157），目标为 NativeUtility 1.3.6、commit `45f50e770e8a8396cf6008ccb6373e8cd3db8571`。

两次业务 Attempt（`65af7945-bb15-4163-b430-239269949243`、`86db234f-32f2-40ae-b946-bcb877943d99`）均由 Unity 返回明确失败：

```text
ENOENT: no such file or directory, scandir
.../Library/PackageCache/.tmp-.../clone/.git/objects/5d
.../Library/PackageCache/.tmp-.../clone/.git/objects/db
```

包没有进入 manifest、lock、PackageCache；对比两次 Attempt 的安装前基线，当前 manifest 和 lock 的内容均一致。失败信息中的“依赖可能已有变化”是旧执行器统一提示，不能作为发生了依赖变化的证据。计划后来被用户取消，保留取消状态。

## 隔离实验

`tests/run_direct_git_probe.py` 创建临时工程，复制工程 manifest 作为只读基线，只放一个直接调用 `Client.Add` 的测试脚本；不包含 ServiceBase、MTP、插件导入和计划调度代码。目标 URL 无内嵌凭据、固定 commit；复用电脑现有 Git 认证。

- 默认设置：`.runtime/direct-git-probe-098540e6/summary.json`，一次 Add 复现相同 `.git/objects/b1` 的 ENOENT。
- 本次进程关闭 `gc.auto` 和 `maintenance.auto`：`.runtime/direct-git-probe-f8015b5a/summary.json`，一次 Add 成功，注册 commit 与目标一致。
- 只对临时工程的 UPM 克隆目录生效：`.runtime/direct-git-probe-c28f5c73/summary.json`，一次 Add 成功，注册 commit 与目标一致。

默认实验的 Git Trace2 显示 fetch 后启动 `git maintenance run --auto --quiet --detach`，随后执行 `repack -d`，与 UPM checkout/目录处理时段重叠；两个规避实验均未运行 maintenance/repack。证据支持 Git 后台对象整理与 UPM 遍历临时 `.git` 的竞争；这是本机复现结论，未声称 Unity 官方已经确认某个缺陷编号。

Git 官方说明自动维护可以后台运行，并会整理对象文件：[maintenance](https://git-scm.com/docs/git-maintenance)、[maintenance.auto / autoDetach 与条件 include](https://git-scm.com/docs/git-config)。完整 trace 和原始日志仅保存在本地，可能包含 Git 认证相关参数，不上传服务或直接公开。

## 已应用的工程级规避

通过 `tools/configure_upm_git_cache.py`，在用户 Git 配置新增一条条件 include，仅匹配：

```text
/Users/chenzixiao/Desktop/UnityWorkspace/Personal/TestService/Library/PackageCache/.tmp-*/clone/.git
```

对应 `.servicebase/upm/git-cache-policy.config` 设置 `gc.auto=0`、`maintenance.auto=false`。它作用于 UPM 随后丢弃的临时克隆；普通仓库、其他工程和项目依赖保持原设置。已保存修改前 Git 配置到 `.runtime/backups/git-upm-cache-20260922-165802-a66faf/`，备份目录/文件权限为 0700/0600。

查看和精确撤销（只删除匹配的 include，不整体覆盖用户 Git 配置）：

```sh
.venv/bin/python tools/configure_upm_git_cache.py --project '/Users/chenzixiao/Desktop/UnityWorkspace/Personal/TestService' show
.venv/bin/python tools/configure_upm_git_cache.py --project '/Users/chenzixiao/Desktop/UnityWorkspace/Personal/TestService' remove
```

这是显式的本机配置工具。执行器不会在每次安装时静默修改全局 Git 配置。其他电脑和工程需要单独验证和配置；该规则依赖当前 UPM 临时目录布局。

## 执行器错误反馈修复

- 在释放 Add/List 请求前保存 `Error.errorCode` 和 `Error.message`。
- 原始错误只进入本地安装日志；网页接收有长度上限、隐藏 URL 凭据和认证头的诊断摘要。
- 明确对比安装前后的 manifest/lock 文件，区分“与安装前一致”“已有变化”“无法读取”，不再统一使用推测性提示。
- 旧日志仍可读取；没有错误字段时提示查原始 Unity 日志，不编造错误。
- 已同步 TestService 的 `InstallJournal.cs`、`UpmExecutor.cs`、`UpmDiagnostics.cs`，Unity 已重编译。旧日志/已取消任务不改写，不创建重试或恢复后续步骤。

验收：179 项 Django 测试通过（含 Git 条件 include 的目录隔离、幂等和精确撤销）。`.runtime/upm-diagnostics-smoke-61157396/result.json` 验证 Unity 2022 的错误持久化、脱敏、清单变化与旧日志兼容。Unity 6 真实 UPM 404 实验通过完整服务回传，网页收到具体 NotFound 和未变更清单提示，Add 只调用一次。

## 流程取舍

本次故障属于 Git 获取阶段；前一次 EDM4U 属于导入后 meta/自清理核对，两者是不同边界。当前证据不足以声称所有包与工程已达到稳定交付，但不需要因此丢弃服务端计划、依赖顺序、工程互斥和域重载日志。

若准备推广到多工程、多电脑，建议下一阶段改造包的分发：服务端按固定 commit 生成不可变 `.tgz`、记录内容哈希，Unity 下载校验后使用本地 tarball 安装。Unity 官方支持 [本地 tarball](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-ui-tarball.html)。这能减少客户端 Git 版本、认证和后台维护的影响；仍需实现来源映射、LFS/子模块完整性、跨电脑路径、缓存保留、版本核对及更新卸载验收，不能只把 URL 换为 file 路径。插件导入的归属和自修改规则仍需独立维护。

本次仅完成故障定位、工程级规避及诊断修复，未切换包分发协议。
