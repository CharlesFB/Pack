# 第三步：独立执行器与单包 UPM 闭环

2026-09-22：NativeUtility 的 Git 临时目录竞争及 UPM 原始错误记录修复见 [Git 安装失败定位](upm-git-cache-diagnosis.md)。

## 代码设计

- `dashboard/installations.py`：固定目标校验、工程权限、幂等建单、持久化回执与只读状态。
- `InstallJob`：工程、请求幂等键、不可变目标；数据库条件唯一约束限制每工程一个 active Job。
- `InstallStep`：首期每 Job 一个安装步骤；`InstallAttempt`：每步骤可有多次人工授权的尝试，保存派发时间、回执序号、重新核对计数及重试原因。
- `InstallEvent`：按 attempt + sequence 唯一保存收到的进度变化；重复相同回执确认，不覆盖原结果。
- `unity/UpmExecutor/`：独立 Editor asmdef，仅依赖 Unity 和 .NET，不引用 MTP 或自动引用第三方预编译程序集。
- `InstallJournal.cs`：`.servicebase/upm/active.json` 原子替换，修改前 flush；终态获服务确认后写 history 并清理 active。
- `JsonMap.cs`：独立只读解析 Unity 的 manifest/lock 字典，拒绝重复键与过深嵌套。
- `project-selection.mjs`：共用工程选择；`installations.js`：单包提交、轮询状态、重新核对。

## 协议与状态

1. Unity 沿用设备注册与鉴权，独立同步 `/api/bridge/upm/sync/`，携带 `protocol=1`、`executor_version=2`、`has_report`、本地诊断和日志回执。`has_report` 处理 JsonUtility 空对象展开行为。
2. 网页 POST `/api/projects/<id>/installations/`，传包名、固定 identifier、UUID request_key。同 key 不允许改目标；重复请求返回原 Job。
3. 首次派发后服务记录 dispatched_at。若客户端没有日志却再次收到已派发任务，进入仅核对模式；即使首次响应丢失也不盲目调用 Add。
4. Unity 接收即落盘，预检 `Client.List(true,true)`，等待请求结束，再保存 manifest/lock 基线和安装意图，然后调用一次 Add。新工程允许安装前没有 lock。
5. 正常请求结束或域重载恢复后，用 manifest 直接依赖、lock 中源/版本/hash、已注册包 source/version/git.hash/isDirectDependency 共同核验。成功还要求编辑器不忙且没有编译错误。
6. 未稳定时最多观察 10 分钟；未知状态进入 needs_review，保持工程锁。网页重新核对只增加 recheck 计数，不生成新 Attempt 或重启 Add。
7. Unity 持续同步终态直到确认；服务已提交但响应丢失时，下一次同回执只确认。终态不能被后续进度覆盖。
8. 心跳 45 秒失联仅显示等待连接；10 分钟无进度仅显示需要核对，不自动结束任务、撤销或再次安装。服务缓存丢失后从 DB 恢复任务。

状态：queued → accepted → running → verifying → succeeded / failed / needs_review。恢复可以直接进入 verifying；needs_review 可重新核对。只有确认未调用 Add 的前置失败，或 Add 明确失败且未满足目标，才能报告 failed。失败不表示所有依赖变更已回滚。

## 目标与功能边界

- Registry：完整 `包名@x.y.z`，支持明确预发布版本，不接受 latest 或版本范围。
- Git：无内嵌凭据的 `https://host/path/repo.git[?path=/子目录]#40位小写commit`。认证由 Unity 所在电脑提供。
- 当前入口独立于原有目录中的安装/卸载占位按钮；尚未接通目录 tag 到 commit 的执行级解析。
- UPM 分支不解析 dependenciesGit，不卸载或回滚。第 5、6 步新增的插件分支见 plugin-imports.md。UPM 可能调整标准间接依赖。
- 不支持本阶段更新 MTP 自身。发现 MTP 的包标识、已知程序集标识或恢复/进度/待提交记录时阻止新安装；不调用 MTP、不改写其记录。当前支持边界为无已识别 MTP 的工程；这不表示已经实现 MTP 严格共存。
- 旧 Bridge 在 active.json 存在期间暂停 MTP 快照读取。无法阻止其他工具或 MTP 自身订阅的 Unity 全局事件，也无法给其他 UPM 客户端加锁。严格共存仍按 mtp-separation.md 单独验收。
- 丢失日志且目标未达成时，首版保留需人工核对的任务；没有强制解锁/盲目重试按钮。
- 磁盘写入失败或日志损坏时执行器停止自动操作，并继续报告本地故障，网页显示在线但未就绪。修复后使用 Tools → ServiceBase → UPM → Reconnect and Recheck 重读日志；不会自动使用可能落后的备份重装。不要删除日志以重试。

## 部署与迁移

1. 运行 `.venv/bin/python manage.py migrate` 后重启自己的服务。
2. 现有 Bridge 用户复制更新后的 `unity/BridgePullClient.cs`，同时把完整 `unity/UpmExecutor/` 目录复制到工程 `Assets/Editor/ServiceBase/UpmExecutor/`，保留 asmdef。新目录自身不依赖根 Bridge 文件；已有桥接的消息/快照能力继续由原脚本提供。
3. URL/token 使用现有 `SERVICEBASE_URL` / `SERVICEBASE_TOKEN`，或 EditorPrefs 的 `ServiceBase.Url` / `ServiceBase.Token`。设备 ID、编辑器 session 与停止开关沿用 ServiceBase 的已有键；全部与 MTP 记录无关。更改配置后可重新加载域或用执行器 Reconnect 菜单；有未结束任务时不得更换服务器归属。
4. `.servicebase/upm/` 加入业务工程 Git 忽略，但不要在清理 Library 时删除。日志包含原始 manifest/lock 的本地基线，可能含工程已有源凭据，不上传服务；将其作为工程恢复数据保护。
5. 不改业务工程现有依赖，也不迁移/双写 MTP 的安装和插件记录。

## 验证

- `.venv/bin/python manage.py test dashboard`：权限、固定目标、幂等、工程互斥、状态不可回退、证据不符拒绝、超时不释放锁、迟到回执、重新核对和空回执兼容。
- `.venv/bin/python tests/run_upm_smoke.py --unity /Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app`：创建隔离工程和本地临时 Registry、独立 SQLite，真实安装仅供测试的包；检查域重载、丢失终态确认后重传与唯一成功事件。退出时关闭临时 Unity 和服务，结果留在 `.runtime/upm-smoke-*/`。
- 此验证不覆盖任意商业插件、全部 Unity 版本、MTP 监听副作用或整机断电。测试范围应随运行证据更新，不把编译通过当成实机闭环通过。

2026-09-15：全量 87 项测试通过。Unity 2022.3.62f2 的 Registry、域重载、丢终态确认、HTTPS Git 认证及故障恢复实机结果见下方验收记录。MTP 严格共存和整机断电不在已通过范围内。

## 第四步补充

人工恢复、版本就绪检查、MTP 阻止边界及最新故障验证结果见 [recovery-validation.md](recovery-validation.md)。旧执行器仍可回传已有任务结果，但不领取新的未派发任务。

启动或重新连接后，执行器先与服务器对齐任务，再推进操作。调用 Add 前另写不可覆盖的 `.servicebase/upm/intents/<attempt-id>` 标记；恢复旧 active.json 时仍只核对。旧版日志没有 intent_protocol 标记，也按仅核对处理。升级不能通过删除日志或意图文件恢复安装。
