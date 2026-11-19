# 插件仓库

当前支持服务器本地制品保存、包到插件映射、完整性校验和网页「插件仓库」只读标签页。可查询分类、插件版本、制品状态及来源关联；接口见 ../docs/development.md。安装、导入、更新、删除接口尚未实现。

## 当前目录与管理索引

仓库根目录直接放 `magictavern` 和可明确归类的第三方插件目录，不再使用 `external-2636` 包装层：

```text
external/
  magictavern/<内部插件>/<版本或unversioned>/<内容哈希>/
  dotween-pro/1.0.335/<内容哈希>/
  odin-inspector/3.1.10.0/<内容哈希>/
  adjust/5.0.6/<内容哈希>/
  edm4u/1.2.177/<内容哈希>/
  ...
```

未知版本使用 `unversioned`，不会从文件名猜版本。版本内保留内容哈希，区分同版本不同制品。特殊版本标签在 JSON 中原样保存，目录名做安全编码。没有未知版本时无需预建空 unversioned 目录。

`groups.json` 按真实顶层目录组织管理分组：1 个 magictavern 分组与 27 个第三方插件分组，总计 58 个构建。`index.json` 是完整权威索引，包含包映射、版本、制品路径和来源。`external-2636-index.json` 保留为来源版本查询索引，文件名中的 2636 不再代表存储包装目录。

本次搬迁更新了 38 个第三方构建的路径、版本索引和报告链接；原有 20 个内部构建、94 个包映射及全部稳定 ID 保留。`layout-migration.json` 记录搬迁状态；`index.before-layout-v2.json` 是旧路径备份，不作为当前索引使用。

以后本地扫描、647 同步和 2636 同步都会刷新 groups.json；2636 新入库制品直接写入插件目录。详见 [2636 版本仓库说明](external-2636-README.md)。下方 647 数据是历史汇总，仓库总数以最新 index.json 为准。

## 最新完整目录扫描

已从 647 读取 91 个目录条目（63 个仓库），按现有发布规则检查各包最高非 debug 版本的完整文件树：87 个有发布版，4 个只有 debug 标签。最新清单有 20 处引用，去重后 19 份制品（16 个 unitypackage、3 个 ZIP），共 36,941,970 字节。

仓库保留 20 份制品（含此前标准标签中的旧版测试 ZIP），共 36,942,235 字节；18 个插件 ID，展示分组见 groups.json，分组不代表制品可互换。当前最高版映射通过 index.json 的 catalog_snapshot.mapping_keys 筛选，不能把全部历史映射当作当前版本。

用户手工清单的 17 个文件全部找到，还发现当前 mtuiframework 的 Animations.unitypackage 与 Demigiant.unitypackage。较旧 testpackage3 的 README ZIP 保留历史记录，不计入最新清单。逐仓库记录与未扫描 debug 版本的原因见 [扫描报告](catalog-report.md)，机器结果见 catalog-scan.json 和 manual-inventory-check.json。

87 个发布条目合并同包名/commit 后是 86 个当前映射；连同保留的旧版本共 94 个。远端来源记录保存仓库 ID、tag、目录 commit 和验证结果；原本本地只扫描 Editor/PluginsPackage 的范围已补齐至完整文件树。

## 最初的本地扫描记录

2026-09-15 从当前工作区 `Pack Test` 的 `Library/PackageCache` 读取，与其 `Packages/packages-lock.json` 的完整 Git commit 匹配：

- 19 个 MagicTavern Git 包的版本映射，包括没有插件的空映射。
- 4 个包关联 6 份 unitypackage，原始文件共 12,704,283 字节。
- 本次没有发现 ZIP。ZIP 解析有测试，尚无此工程真实 ZIP 的验证。
- 不代表全部远端仓库/历史版本；不代表这 6 个插件已经导入工程。
- 本地缓存可能被人为修改，来源标记为 `local_locked_package_cache`，并保存 package.json 和制品哈希；未宣称已与 GitLab 远端逐字节对账。

关联内容：

- `com.magictavern.langtool` 1.4.12 → Odin Inspector ConfigTool。
- `com.magictavern.minigame` 0.6.11 → DOTween。
- `com.magictavern.sdk.ads` 1.0.26 → AppLovin MAX。
- `com.magictavern.sdk.socialaccount` 1.0.26 → Facebook Unity SDK、Google Play Games、Google Sign-In。

完整 commit、源包内路径和构建哈希在 `index.json`。

## 文件组织

`magictavern/<plugin-id>/<version-or-unversioned>/<sha256>/` 下保存：

- `plugin.json`：name、format、version、published_at、unity、description 六项展示信息。
- `manifest.json`：schema_version、稳定 ID、SHA-256、制品大小、逐项资源路径/哈希/GUID、异常清单。
- `artifact.unitypackage` 或 `artifact.zip`：源文件原样复制。

`source-metadata.json` 是扫描工具的文件名到稳定插件 ID/展示信息的映射。后续新文件名需要显式登记 ID，不自动猜测两个文件是否同一插件。

目前尚未核实插件自身版本、发布时间、Unity 要求和描述，统一留空；不把源文件修改时间当发布时间，不把包版本当插件版本，也不自动将文件名数字当成权威版本。技术索引统一标记 `needs_review`，包映射 `execution_ready=false`，required/phase 未确认。

`index.json` 是当前唯一发布索引，包含：

- `plugins`：已入库构建。
- `package_mappings`：包完整名称 + commit → 插件构建；同时保存包声明版本。
- `project_snapshots`：本次工程扫描范围和映射引用，工程路径只保存 SHA-256。

这些 JSON 没有 Git 认证地址或 token，也不复制 MTP 配置和业务记录。数据库暂不增加第二份可编辑索引。

## 现有制品需审查的问题

- Facebook Unity SDK 含 19 条 `Packages/com.unity.ugui/...` 路径，记录在 `non_assets_paths`，不能直接按“只写 Assets”的插件执行。
- Google Play Games 有 1 条资源缺少 `asset.meta`，记录在 `missing_meta_paths`，未来导入验收要先处理。
- Google Play Games 包含 macOS AppleDouble 附加项；保留原制品，解析索引时忽略并计数，不把它们认作 Unity 资源。

这些制品仅入库，不自动修复/重打包。归档中若含路径穿越、符号链接、重复资源路径或无法解析的结构，扫描拒绝发布。

## 使用

从 ServiceBase 目录执行：

```sh
.venv/bin/python manage.py plugin_repository import-project --project '/Users/chenzixiao/Desktop/UnityWorkspace/Personal/Pack Test'
.venv/bin/python manage.py plugin_repository verify
.venv/bin/python manage.py test dashboard.test_plugin_repository
```

完整远端更新分为扫描和下载两步，沿用服务器现有 GitLab 凭据配置，不接触 Unity/MTP 运行时：

```sh
.venv/bin/python manage.py plugin_catalog scan
.venv/bin/python manage.py plugin_catalog import
.venv/bin/python manage.py plugin_repository verify
```

安装某个发布版时，其依赖可能固定在较旧 Tag；这些版本不在“各包最高发布版”扫描范围内。按精确版本补齐映射可执行：

```sh
.venv/bin/python manage.py plugin_catalog import-refs \
  --package-ref 618:base-1.0.10 \
  --package-ref 680:nativeutility-1.3.6 \
  --package-ref 759:devicemanager-1.0.2
```

该命令保存 `dependency-scan.json`，按固定 commit 扫描完整文件树、验证并入库插件；追加历史映射，保留原最高发布版选择。2026-09-22 已补齐上述三个依赖版本，当前总计 97 条包映射，仍为 58 个插件构建。

扫描实时仓库 tags，沿用现有 MTP 的发布版本口径：排除包含 debug 的标签，按最后一段数字版本取最高，包含带版本的命名分支；相同数字版本优先标准 `<PackageName>-版本` 标签。不调用 MTP 代码。目录中的同仓库不同包分别选版本；完整分页扫描文件树并保存空结果/无发布版本状态。扫描错误不会冒充无插件；存在错误时禁止导入发布。新增文件名需先在 source-metadata.json 登记稳定 ID。

下载固定到扫描 commit，按 Git blob 去重下载并核验其校验和；文件再按 SHA-256 入库。同名不同内容保留不同构建。同一步扫描的全部下载与归档校验成功后才提交 index.json；迟到更新不会替换运行中的固定计划。当前不支持 Git LFS 指针自动解析，如遇到将明确失败。

可通过 `--root` 指定外部持久化仓库目录，通过 `--metadata` 指定插件身份/元数据源。仓库不得位于源 Unity 工程内。

工具只读锁定的 `com.magictavern.*` Git 包的 `Editor/PluginsPackage`。缺少或存在多个匹配缓存会拒绝扫描，避免把旧缓存误当当前版本；不会扫描任意 Assets、未解析包或嵌入式包。重复运行不重复添加条目，同一 commit 的映射发生变化会拒绝覆盖。

进程级文件锁串行写入，先写制品并核验，再原子替换索引；失败时保留旧索引。失败或中断可能留下未被索引引用的制品目录，不会将它们视为已发布，也不自动删除。源锁文件在扫描期间变化会拒绝发布。

当前发布构建不可原地覆盖；修改已有构建元数据时工具会拒绝冲突。后续人工确认版本或改 ID 应通过专门的索引迁移处理，保留旧引用；本轮不提供仓库编辑 UI。

制品文件已加入 Git/Docker 构建忽略规则，不能只备份源码：要同时备份完整 external 目录（含制品和索引）。部署时应挂持久化目录，本轮未改线上部署。
