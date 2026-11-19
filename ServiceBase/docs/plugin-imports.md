# 第 5、6 步：单条插件导入

**当前行为以 [v10 插件执行与归档清单](plugin-execution-v10.md) 为准。** 下文为历史实现记录；导入后的内容、数量、meta 核验与失败占锁已被 v10 替代。

2026-09-22：v9 补丁修正 Unity 导入后 meta 变化造成核对无法结束的问题，见下节。v6 已补充当前状态核对和单插件删除，升级与当前行为见 [插件管理](plugin-management.md)。下文 v5 验收作为历史记录保留。

## Unity 导入结果核对修复（2026-09-22）

- 原始归档与暂存文件仍按制品 SHA-256 严格验证。unitypackage 首次导入后，核对文件内容、GUID、AssetDatabase 路径、编译状态，并等待两次间隔 5 秒的稳定快照；允许 Unity/插件重写 importer meta，分别保存 `source_meta_sha256` 和实际 `meta_sha256`。ZIP 保持原校验规则。
- 新归属记录标记 `verification: unity-import-v1`。服务端验证原制品 meta 哈希、内容及 GUID；旧回执继续严格验证。归属记录一旦保存就冻结，丢回执、域重载、重新核对均不能将后续修改再次吸收进基线。后续状态扫描和删除仍使用已保存的实际哈希，保护用户修改和原有文件。
- EDM4U 制品 `6db6b031d4fbd18a73e8b56455245b24355a9cfc558b7ae042326503bb01bc2a` 会自行清理旧 `play-services-resolver_v1.2.137.0.txt`。只允许该确切制品、确切内容哈希的标记及其两个生成父目录在导入前均不存在、导入后连 meta 都不存在时列入 `omitted_files`。DLL、当前版本清单及其他资源仍必须完整匹配；不根据插件名称放宽。20 项原始基线保留，正常结果的归属文件为 17 项，后续删除不会认领已清理项。
- `importPackageCompleted` 在域重载时可能丢失；不再因内存 `importing` 标记一直等待。实际证据稳定后可完成；不完整结果在十分钟上限后进入“需要核对”，给出缺失/内容/GUID/meta 原因，不重新导入。持续变化的快照另有两分钟观察上限。
- 同一构建重复导入支持已经归档的 importer meta，并保留首次归属，不将服务创建的文件变成“导入前已有文件”。没有对应归属证据的覆盖冲突继续阻止。
- 先更新服务端，再将 `PluginImporter.cs` 和新增 `PluginImportVerification.cs` 部署到工程的完整独立执行器目录。已写入的旧任务通过“重新核对”恢复，不修改导入意图或重置任务。

测试：`tests/run_plugin_smoke.py --edm --drop-callbacks --unity /Applications/Unity/Hub/Editor/6000.3.19f1/Unity.app` 使用仓库中同一制品、隔离服务及临时工程，覆盖真实 meta 重写、历史标记清理、重复导入归属、回调丢失（含无域重载的内存标记）、服务端终态确认丢失和 ZIP 回归。

验收证据：178 项 Django 测试通过；`.runtime/plugin-smoke-e2061bf5/result.json` 中 Unity 6 的 EDM4U 两次导入、ZIP 两次导入全部成功；`.runtime/git-metadata-smoke-b9e94e92/result.json` 确认完整执行器兼容 Unity 2022。已更新 Docker 及 TestService 的两份核对脚本，原 Attempt `c4f95f93-c942-4d89-976c-0b4bb6251160` 通过重新核对成功归档；归属扫描 17/17 匹配，缺失和修改均为 0。恢复前后插件资源、meta、manifest、lock、意图文件哈希全部未变。任务锁释放，用户取消状态和后续六个跳过步骤保持；备份及恢复证据保存在 `.runtime/backups/testservice-before-plugin-verification-20260922-162723/`。

## 代码设计和部署

- `dashboard/plugin_delivery.py`：从精确 plugin_id + build_id 读取仓库，重新校验 SHA-256、大小和归档结构，生成不可变 recipe 与下载 ZIP。
- `dashboard/plugin_installations.py`：设备及工程会话限定的下载、文件核验证据保存、工程插件记录查询。
- `dashboard/installations.py`：沿用 Job / Step / Attempt 和每工程唯一 active Job，插件与 UPM 不能同时修改同一工程。
- `unity/UpmExecutor/PluginImporter.cs`：独立导入器，复用原有日志、意图标记和回执协议，不引用 MTP。执行器版本 5 接收网页预选的 ZIP 根目录并自动导入；版本 2 仍可执行 UPM，版本 3、4 仍可导入 unitypackage，不能领取网页预选目录的新 ZIP 任务。
- `dashboard/project_directories.py` + `unity/UpmExecutor/ProjectDirectories.cs`：通过独立执行器读取指定工程的一层资源目录，服务不直接浏览工程机器的磁盘。
- `PluginReceipt` 保存每次成功核对的不可变文件证据；`PluginInstallation` 索引工程、插件、构建及导入时核验时间。
- 插件页按所选工程显示记录、根目录和导入入口；上方“安装 / 导入任务”显示共用进度、失败原因与重新核对。

服务端执行 `.venv/bin/python manage.py migrate`（最新 0009，保存所选目录），然后自行重启服务。Unity 整体替换 `unity/UpmExecutor/` 目录（包括 asmdef、PluginImporter.cs 和 ProjectDirectories.cs，并删除旧 ZipDestinationWindow.cs）；现有 Bridge 保持第四步的版本。不要只复制入口脚本。

## 执行流程

1. 网页选定精确构建，服务器重新检查源制品并生成固定目标。目标包括原制品哈希、下载包哈希/大小、recipe 哈希和资源数量；重复同一请求 UUID 不会创建新任务。
2. Unity 使用设备凭据及当前工程会话 POST 下载。下载只发往已配置服务，不跟随重定向；完成后检查大小、SHA-256，并逐项验证暂存文件及 recipe。
3. 所有目标限制在 Assets 下。保存每个资源的路径、GUID、文件/meta 哈希、是否文件夹、是否原来存在及原始哈希。检查文件/目录冲突、GUID 指向其他路径、符号链接及原有文件完整性。
4. 不同内容的覆盖在预检阶段失败；相同内容允许共享。没有自动覆盖选择、用户修改覆盖或混合 UPM manifest 导入。
5. 保存完整基线和不可覆盖的安装意图标记后才写工程。unitypackage 调用一次 `AssetDatabase.ImportPackage(..., false)`，记录 completed/cancelled/failed 回调；ZIP 按已确认路径写入文件，保留 meta。写入期间暂停自动刷新，结束后统一刷新。
6. 等待 Unity 空闲、编译通过，再逐文件核对内容、meta 哈希、GUID 与 AssetDatabase 路径。重载恢复只核对，不再次导入。回调本身和目录存在都不能证明成功。
7. 本地原子保存归属记录，服务器幂等保存对应证据，之后才提交成功回执。相同构建重复导入保留首次文件归属，同时在任务日志保留本次导入前基线。

## ZIP 在网页预先选择根目录（2026-09-20 调整）

新 ZIP 任务不再需要服务端预设布局，也不再打开 Unity 交互窗口。网页点击导入后：

1. 网页弹出目录选择框，按需浏览目标工程的 `Assets` 及其子目录，支持上一级和输入完整资源路径后读取。目录必须现存且被 Unity 识别；不创建目录。
2. 独立执行器通过 sync 接收只读目录请求，每次仅返回一层文件夹名称，不上传文件内容、不调用 MTP。单次最多展示 200 个子目录，最多检查 2000 个目录条目；超限明确提示，可输入完整路径继续读取。
3. 目录请求与工程及当前会话绑定，在缓存保留 90 秒；网页等待最多 45 秒，超时、重连或缓存丢失后允许重新读取。浏览请求不会占安装锁、不会修改工程；生产继续使用共享 Redis。
4. 点击“导入到此目录”后，根目录和制品一起保存为不可变目标，初始 Attempt 同时保存根目录。确认前取消或按 Esc 只关闭网页弹窗，不创建任务。工程切换或会话变更也关闭旧选择。
5. Unity 接收后立即持久化目录，校验目录仍位于当前工程 Assets 内且无链接，再自动下载、预检、写入、核验和回传。Unity 无选目录交互，batchmode 同样可执行。
6. 域重载、编辑器重启、页面重开后沿用已保存的目录。明确失败后手动重试也保持相同目录；要改目录需新建任务。

仅允许当前工程的 `Assets` 或其子目录，拒绝其他工程、工程根目录、Packages、ProjectSettings 和符号链接。目录在选择后消失或变成链接时，在写入前失败。网页选择不等于提前核验整个归档或保证可以覆盖原文件，既有文件/GUID 冲突预检仍生效。

兼容旧任务：已有目录的历史任务继续按原路径恢复；版本 4 创建、尚未选择目录且本地日志完整的任务，在版本 5 下会以未写入的预检失败结束，随后可从网页重新导入。日志缺失或已有写入意图仍保持核对，不推断“没有执行”。旧目的地回执接口只保留历史恢复兼容，不能更改新任务的网页选择。

完整保留 ZIP 外层结构，不自动删去首层目录；ZIP 原有 `Assets/Foo` 选择 Assets 后会变成 `Assets/Assets/Foo`，网页说明保留这一结构。本次未提供去掉外层目录选项。路径穿越、绝对路径、控制字符、大小写重复路径、链接和特殊文件仍拒绝；macOS 的 `.DS_Store`、`._*`、`__MACOSX` 元数据不作为 Unity 资源导入。

`external/import-layouts.json` 不再影响新任务，原文件保留用于历史追溯；旧 `plugin_import_layout` 命令返回迁移说明，不再修改配置。已派发的旧版 ZIP 任务保留原固定目标。BuildConfig 等若需要工程 Assets 以外的目录，仍不在本阶段支持范围内。

ZIP 缺失的 meta 以及未包含的父目录 meta 在服务端生成稳定 GUID（插件 ID + 路径）。已有完整且相同内容的资源保留工程内 meta，不改 GUID。unitypackage 缺失资源 meta、含非 Assets 路径或携带 UPM manifest 时拒绝导入，需要先整理源制品。

## 记录与恢复

- `.servicebase/upm/active.json` / `history/`：单次操作、导入前基线、回调和核验进度。
- `.servicebase/upm/intents/`：已进入修改阶段的不可覆盖标记。
- `.servicebase/plugins/cache/<attempt>/`：下载包及暂存文件。
- `.servicebase/plugins/records/<plugin>-<build>[-<目录摘要>].json`：文件归属证据。原来不存在的资源才可作为将来删除的候选；原有文件和其他插件引用仍需保留。
- 服务端 `.runtime/plugin-deliveries/`：已派发任务需要的固定下载包和 recipe。应和任务数据库一起保存，不要在存在未结束任务时清理。

未开始写入的明确预检/下载失败可走现有“填写原因并重试”。写入后异常、取消后的文件不完整、日志缺失/损坏、十分钟仍不匹配均保留工程锁并提示核对；修复后使用“重新核对”，不会重装。重连菜单沿用第四步。

ZIP 归属记录同时保存所选目录和实际资源路径；目录摘要避免另一次选择覆盖旧目录的本地归属记录。服务端插件列表展示该构建最近一次导入的目录，历史尝试和回执仍保留。

网页“导入时已核验”是带时间的历史记录，不声称已检测后续手动修改。v6 已新增删除和当前状态扫描（见插件管理文档）；更新仍未开发，根目录展示不意味着可直接递归删除整个目录。

## 限制与验收

- 继续执行第四步 MTP 阻止边界，未实现严格共存。
- 单次展开资源上限 2 GiB；最多 10000 项，另按路径长度和证据大小约束，避免超过接口请求限制。下载包与暂存目录需要额外磁盘空间。
- 使用原 unitypackage；首次导入的 meta 核对按上文 v9 补丁处理。未知的资源缺失或内容变化仍进入核对。插件脚本额外产生的文件不会自动纳入可删除归属；不能把此阶段当作任意商业插件更新器。单插件更新执行器仍按其独立的替换与备份规则核对，本补丁没有放宽更新证据。
- 现有仓库 Unity 要求均未填写，不能据此保证兼容性；当前实机版本为 Unity 2022.3.62f2。
- 不是跨服务端、文件系统和 Unity 的原子事务。不完整结果保持阻塞，不自动回滚或删除文件。

测试命令：

```sh
.venv/bin/python manage.py test dashboard
.venv/bin/python tests/run_plugin_smoke.py
.venv/bin/python tests/run_plugin_smoke.py --partial
```

2026-09-15：95 项自动化测试通过；迁移、Django 检查和前端模块语法检查通过。实机证据：

- `.runtime/plugin-smoke-54596de7/result.json`：unitypackage + ZIP 成功，自动 meta、脚本域重载、丢终态确认恢复、重复导入归属保留、修改文件预检阻止。
- `.runtime/plugin-smoke-db31e65c/result.json`：阻断 ZIP 成功回执后杀死测试编辑器，移除一个测试文件以构造不完整状态，恢复日志到核对阶段并推进超时时间；重启后保留锁、重复导入零次、不安全重试被拒绝。此为故障注入，不等同于真实断电或在任意字节处中断写入。

全部验收使用隔离数据库、自制制品和临时工程，未在业务工程导入商业插件。

扩展后的 UPM 回归：`.runtime/upm-smoke-1789468464/result.json`，Registry 安装、域重载和丢终态确认恢复再次通过。

## 历史验收：Unity 手动目录选择（已由网页选择替代）

- 98 项自动化测试通过：路径边界、设备会话、目录不可变、旧执行器拒领、等待不超时、取消释放锁、导入后禁止取消、按实际落点验证回执。
- `.runtime/plugin-smoke-b5003d20/result.json`：选 Assets/Chosen 后 ZIP 导入成功，重复导入保留归属，文件冲突阻止。
- `.runtime/plugin-smoke-bc0b1214/result.json`：取消后无资源写入、无修改意图标记，任务锁释放。
- `.runtime/plugin-smoke-dd769035/result.json`：选定目录后终止并重启测试 Unity，目录只确认一次，恢复下载、导入和核验成功。同时修复 JsonUtility 将未准备的插件状态还原为空对象的问题；清单必须非空且数量匹配才可执行。
- `.runtime/plugin-smoke-a293b469/result.json`：使用真实 Unity 窗口完成原生选目录、落点预览、确认和导入；预览窗口在原生选择框关闭后主动获得焦点。

上述为旧版本历史证据。当前测试直接通过网页 API 指定目录，不再生成确认/取消适配器；生产目录窗口已移除。


## 2026-09-20 网页目录选择验收

- 103 项 Django 自动化测试通过，9 项前端既有测试通过；Django 检查、迁移一致性和模块语法检查通过。本次没有新增数据库迁移。
- `.runtime/plugin-smoke-f3a08087/result.json`：真实 Unity 2022.3.62f2 batchmode，独立执行器读取 Assets 目录；网页 API 预选根目录后自动导入，脚本域重载、丢确认恢复、重复导入保留归属、用户改动冲突阻止均通过。
- `.runtime/plugin-smoke-f517130d/result.json`：真实网页浏览 Assets → Chosen、取消（数据库中无 ZIP 任务）、再次选择并确认；随后在下载前终止并重启测试 Unity，沿用 Assets/Chosen 自动导入成功。仅一个 ZIP Attempt，重启后只有一次 ZIP 导入调用。页面重开仍显示原目录和完成状态。
- 当前批处理执行器没有用于选目录的测试适配器，也没有目录交互窗口。回执协议明确区分无目录回执与 JsonUtility 序列化的空对象。
- 两次成功验收均使用临时服务、临时数据库、自制插件和临时工程，结束后关闭测试进程；未启动或停止业务服务，未向业务工程部署。

补充命令：`tests/run_plugin_smoke.py --choice-restart` 验证目录保存后的重启恢复；加 `--web-ui` 后需在输出的临时网页中选择 ZIP 并提交，适合浏览器交互验收。
