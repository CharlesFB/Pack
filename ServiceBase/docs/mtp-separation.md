# MTP 分离边界与迁移记录

日期：2026-09-15。

## 用户确定的方向

1. 未来新增的 Unity 安装器、导入器和卸载器不依赖 MTP 的类型、反射入口、JSON 工具、SharpZipLib 程序集或业务初始化。
2. 服务的安装/卸载/更新不调用 MTP 业务；记录与锁由 ServiceBase 自己维护。工程已存在的依赖暂不迁移。
3. 两侧可以展示同一工程的实际包状态；MTP 展示可能存在旧缓存，不通过触发 MTP 安装/刷新业务来同步。
4. 插件仓库阶段先建立服务端制品仓库和版本映射；现已新增独立单包 UPM 执行器，详见 single-upm-installation.md。现有只读快照适配器的旧 MTP 反射依赖属于后续迁移范围，不是新执行器的依赖。

## 不能省略的边界

“不调用 MTP”不等于“能够阻止仍在加载的 MTP 观察 Unity 全局事件”。静态检查发现：

- `MTPManager.cs` 带 InitializeOnLoad，订阅编辑器 focusChanged；会检查 MTP 安装记录时间和目录缓存过期并刷新。
- `MTPManager.Recovery.cs` 存在恢复未结束 MTP 安装的入口；未来服务开始前需识别 MTP 未收尾操作。
- `MTPUninstallMonitor.cs` 订阅 PackageManager.Events.registeringPackages，检测 MTP 自身被移除时清理 MTP 持久化文件与编译宏。

因此目前不能承诺任意 MTP 版本在所有安装变化中完全没有业务副作用。未来要达到严格隔离，需验证已安装 MTP 版本的监听器：服务不得接管未完成的 MTP 操作；卸载/更新 MTP 本身单独处理；必要时 MTP 增加明确的共存模式，或迁移完成后停用 MTP。ServiceBase 新执行器仍不通过反射禁用/调用 MTP。

仓库构建阶段没有修改 MTP、Bridge 或 Unity 工程，也没有调用 Unity API，因此仓库构建不会触发这些业务。第三步新增了独立执行器和 Bridge 的快照暂缓逻辑，实际安装在隔离工程验证。静态检查不是运行隔离验收。

## 本轮共享/读取记录

- 读取 Unity 官方 `Packages/packages-lock.json`：只使用包名、source、完整 hash 确定扫描版本；不写入。
- 读取匹配缓存下的 `package.json` 和 `Editor/PluginsPackage`；原制品复制到服务器，不在 Unity 内解包或导入。
- 没有共用 MTP 可写记录。ServiceBase 新仓库的唯一可写源是 `external`。

## 未来可能迁移的旧记录

这些是候选输入，不是本轮复制或双写的文件：

- `Packages/upm-install.json`：MTP 根包/安装记录。
- `Library/upm-dependencies.json`：MTP 依赖记录，可能不存在或被清理。
- `Packages/plugin-assets.json`：当前 Pack Test 的插件记录；需要独立解析器核验实际 schema 与归属可信度。
- 本地包清单存在版本差异：当前 MTP 源码常量为 `Packages/ump-lock.json`，Pack Test 存在 `Packages/upm-local.json`，不能把名称视为通用协议。
- 配置文件也有版本差异，不复用其凭据或把它们复制到插件仓库。
- `Library/mtp-install-recovery.json`：MTP 执行恢复记录，仅用于未来识别忙碌/未收尾状态，不能被 ServiceBase 改写以接管。

推荐单向迁移：独立解析旧 JSON → 保存来源/schema/时间和文件摘要 → 与实际工程核验 → 写入 ServiceBase 独立记录。避免双写：MTP 可能刷新、删除自己的记录，尤其卸载自身时；它们不能成为 ServiceBase 的唯一恢复依据。

后续执行器独立记录放工程 `.servicebase/`；仍使用 Unity 标准 UPM 状态作为包安装事实。清理 Library、卸载 MTP 或 MTP 自己刷新不得删除 ServiceBase 的执行状态。

## 第三步实现后的共享记录清单

- 新执行器使用 `.servicebase/upm/active.json` 和 `history/<attempt-id>.json`，不复用 MTP 可写文件。
- 只读复用 ServiceBase 的 EditorPrefs：`ServiceBase.Url`、`ServiceBase.Token`；沿用或首次创建 `ServiceBase.MachineId`。
- 共用 ServiceBase 的 SessionState：`ServiceBase.Bridge.Session` 与 `ServiceBase.Bridge.Enabled`。这些键属于 ServiceBase，不属于 MTP；以后更换身份或配置存储时需一起迁移。
- 仅检查 `Library/mtp-install-recovery.json` 是否存在，存在则不开始服务安装；不解析、修改或代替 MTP 完成旧任务。
- 原 Bridge 在独立执行器日志存在期间暂缓采集 MTP 快照；不以安装成功为理由调用 MTP 刷新业务。
- 对 MTP 全局监听的限制仍然成立；本轮实机闭环在不安装 MTP 的隔离工程验证，不宣称已验证严格共存。

## 第四步明确的执行边界

现阶段发现 `com.magictavern.mtpmanager` 包、`Magictavern.FrontEngine.MTPManager` 程序集，或 `Library/mtp-install-recovery.json`、`mtp-install-progress.json`、`mtp-pending-manifest.json` 任一记录时，服务执行器不启动新的安装。只检查身份和文件存在，不反射调用 MTP 业务。MTP 的焦点与包注册监听仍存在，当前没有以修改其代码或关闭其监听的方式宣称严格共存。业务工程仍使用 MTP 时可继续浏览仓库；服务安装须待共存协议验收后开放。

## 2026-09-22 插件管理记录

独立执行器 v6 增加 `.servicebase/plugins/status.json`、`removed/` 和 `trash/`，均为 ServiceBase 自有数据。实时状态从自身 `records/` 和工程资源核对后上报，不读取或接管 `Packages/plugin-assets.json`，不调用 MTP 导入/删除业务。旧 MTP 记录的归属、版本和共享文件迁移仍需单独开发。已识别 MTP 工程继续允许只读核对，禁止新修改操作。

## 2026-09-22 v9 更新

Bridge 已停用正常采集中的全部 MTP 读取；旧适配器仅保留作历史测试。Assets 本地包由独立扫描识别。旧根包、本地包、依赖及插件路径清单仅在用户主动发起预览后读取，确认所选记录后写入 `.servicebase/migrations/records/`，不迁移凭据，不改写 MTP 文件或资源，不授予未知旧文件删除权限。详见 [可选迁移](legacy-migration.md)。原资源操作的共存拦截继续保留。
