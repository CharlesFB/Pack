# Unity Bridge

当前独立执行器为 **v10**：插件导入按执行结果结束，失败可跳过继续安装，删除按原归档清单处理。升级请完整替换 `UpmExecutor/` 并保留已有 `.meta`；无需新增数据库迁移。详见 [v10 插件执行与恢复](../docs/plugin-execution-v10.md)。

这是 ServiceBase 的 Unity Editor 客户端。完整接入流程见 [主 README 第 2～5 节](../README.md#2-同事把-bridge-放进-unity-工程)。

## 安装与连接

1. 将本目录全部脚本及 **`UpmExecutor/` 子目录（包含 asmdef）**复制到目标工程的 `Assets/Editor/ServiceBase/`。
2. 等待编译完成。升级时替换所有旧文件，不保留其他目录下的重复 Bridge 类。
3. 打开 **Tools → ServiceBase → Settings**。
4. 填写管理员提供的 HTTPS 根地址及该电脑的 ServiceBase 设备 token。
5. 点击 **保存并重新连接**。
6. 浏览器使用自己的账号登录服务，再通过“绑定本机设备”填写同一个 token。管理员无需绑定即可查看工程。

不修改 MTP 源码，不需要 MTP 程序集引用或额外条件宏。当前没有制作 UPM package/plugin，不能只复制入口脚本。

## 设置和多工程

服务器 URL 与设备 token 保存在本机 EditorPrefs。同一电脑、同一操作系统用户下的多个工程可使用同一个设备 token；各个正在运行的编辑器需分别重新连接，才能应用修改后的设置。

`SERVICEBASE_URL` 和 `SERVICEBASE_TOKEN` 环境变量优先于 EditorPrefs。环境变量变化后应重启 Unity。

菜单还提供 Start、Stop 和 Open Dashboard。首次导入可自动启动；手动 Stop 状态在当前编辑器会话的域重载后仍保留。

Bridge v2 只主动发出 HTTPS 请求，不在开发电脑开监听端口。外部服务不能使用 `http://127.0.0.1:8000`；该默认值仅供服务与 Unity 同机开发。

## 历史 MTP 适配代码（v9 已停用读取）

- UPM 中的 MTP `1.3.19` 及以前：只读 `Library/upm-dependencies.json`，通过旧版独立 JSON 方法反射读取，不初始化旧管理器。
- `1.3.20` 起：使用新版服务导出 API；实际版本仍需具备兼容接口。
- 没有 UPM 包，但存在新版服务接口：支持直接放在 Assets 中的 MTP 开发版。
- 版本无法识别、类型或字段不兼容：返回明确状态并停止读取，不猜测其他私有字段。

已验证旧版 `1.3.19` 的真实数据模型与当前重构版导出路径，不代表全部历史版本均已实机验证。缺失或损坏的旧缓存需要在 MTP 自身处理。旧版 Revision 按 tag 处理，不伪造 commit。

v9 正常运行不再调用以下新旧适配器，保留源码用于历史兼容测试。

完整包目录由 **Python 服务端**从 GitLab 647 仓库读取；Unity 当前只上传工程记录和安装依据。UPM 已注册信息、本地 package.json 与 MTP 记录可能存在差异，网页会显示状态依据；并非逐个文件校验完整安装结果。

## 脚本分工

- `ServiceBaseBridge.cs`：编辑器生命周期、设备/工程标识、启动与停止菜单。
- `BridgeSettings.cs`：服务器地址及设备 token 配置窗口。
- `BridgePullClient.cs`：注册、轮询、快照上传、失败退避、任务回执与域重载恢复。
- `MtpSnapshot.cs`：用于网络传输的快照数据结构。
- `MtpPackageDetector.cs`：从 UPM 注册信息识别 MTP 版本。
- `MtpSnapshotReader.cs`：选择适配器，处理初始化、不兼容和错误状态。
- `ModernMtpReader.cs`：新版 MTP 服务导出及缓存元数据读取。
- `LegacyMtpReader.cs`：旧版依赖缓存的只读反射适配。
- `MtpReflection.cs`：字段访问、依赖转换和地址移除。
- `PackageMetadataReader.cs`：包展示字段与实际安装依据补充。
- `MtpCatalogReader.cs`：保留的本地目录兼容读取器，当前常规快照采集不调用它。

## 当前任务边界

新增的 `UpmExecutor/` 是独立 Editor 程序集，整个目录（含 asmdef）复制到 `Assets/Editor/ServiceBase/UpmExecutor/` 即可部署单包执行器；现有 Bridge 用户还需更新 `BridgePullClient.cs` 以暂停执行期间的 MTP 快照读取。安装协议、恢复日志、共用配置键及验证范围见 [单包安装设计](../docs/single-upm-installation.md)。首个实机验证版本为 Unity 2022.3.62f2。

消息调试只向 Console 输出字符串并返回 mock 结果，不安装、更新或卸载包。快照刷新也是只读操作。

任务可能重复投递，Bridge 通过有限长度回执记录去重，并用 SessionState 跨域重载恢复。进程崩溃等情况仍可能重复输出日志，不能把当前机制当作未来安装操作的严格只执行一次保证。

第四步升级需整体替换 `UpmExecutor/`（包含新增 `MtpBoundary.cs`），服务端执行 migration 0007。旧执行器仍可回传原任务，但不能领取新任务。已识别 MTP 的工程仍可浏览，只会阻止服务发起新安装；严格共存另行开发。详见[恢复验收](../docs/recovery-validation.md)。

第 5、6 步新增独立 `PluginImporter.cs`，首次版本为 3，当前网页目录选择版本为 5。升级需整体替换 `UpmExecutor/`，服务端执行最新 migration 0009；详见[插件导入设计与验收](../docs/plugin-imports.md)。

2026-09-20 调整：执行器版本 5 新增 `ProjectDirectories.cs`，网页浏览工程 Assets 并预选 ZIP 解压根目录，Unity 全程自动执行。整体替换执行器目录，删除旧 `ZipDestinationWindow.cs`（及其 meta），不要只覆盖同名文件，否则旧窗口会引用已删除的方法导致编译失败。服务端沿用迁移 0009，无新增迁移。新 ZIP 任务不再读取旧的 import-layouts.json。

2026-09-22：该阶段执行器为版本 6，增加 `PluginInventory.cs` 与 `PluginRemover.cs`。支持 Unity 本地记录核对、实时状态回传、按导入目录删除和备份移出；整体更新执行器，服务端应用迁移 0010。保留已存在的 `.meta`，并按上一段移除已废弃的原生 ZIP 目录窗口。旧的 MTP 记录不自动接管，详情见[插件管理](../docs/plugin-management.md)。

2026-09-22 第 7、8 步：该阶段执行器为 **v7**，新增 `ProjectPackageState.cs`。独立上传工程包基线，执行服务端固定的单步骤队列，最后核对全图与插件。整体更新 `UpmExecutor/`，服务端应用迁移 **0011**。执行器不解析整队依赖、不弹出 ZIP 目录选择；计划及插件选择由网页确认。迁移、故障恢复及实机证据见 [安装计划](../docs/installation-plans.md)。

2026-09-22 第 9 步：当前执行器为 **v8**，新增 `PackageRemover.cs` 和 `PluginUpdater.cs`，支持单条 UPM 卸载与插件替换、备份和恢复核对。整体更新 `UpmExecutor/`，保留已有 `.meta`；服务端应用迁移 **0012**。正式业务工程本轮未自动部署。参见 [包维护设计、恢复和验收](../docs/package-maintenance.md)。

2026-09-22 独立状态升级：当前执行器为 **v9**，新增 `AssetsPackages.cs` 与 `LegacyMigration.cs`。整体更新脚本后，Bridge 不再读取 MTP；Assets 扫描与用户主动确认的旧记录迁移见 [接入与恢复说明](../docs/legacy-migration.md)。服务端需要迁移 **0013**。
