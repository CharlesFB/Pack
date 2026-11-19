# 2636 External 插件版本仓库

已完成：27 个插件、38 个版本，共 2,127,209,908 字节（约 1.98 GiB）。[入库结果与版本列表](external-2636-report.md)。

## 来源与版本规则

按用户提供的 MTP `extra-plugins.json` 清单导入全部版本，源清单的必要字段保存在 `external-2636-request.json`。下载规则与 MTP external 一致：仓库 2636，`main` 分支，路径 `plugins/<插件名>/<版本标签>/<文件名>`。

服务将 main 固定为一个 commit 后读取完整分页文件树、元数据与制品，不在下载期间跟随分支变化。此处版本来自目录索引，不是 Git tag；保留 `9.3-Unity6000`、`1.82 - unity6000`、`4.1 with example` 等完整标签及清单原始顺序，不强行排序成语义版本或选择单一最高版。

输入清单共有 27 个插件、38 个版本文件；是否全部成功入库以 `index.json.external_catalogs["2636"]` 和校验命令为准。远端只存在 package.json、没有制品的其他插件不虚构为可下载版本；不在输入清单中的额外归档列入 additional_remote_archives，不擅自安装或导入。

## 存储与索引

- 制品：仓库根下的 `<第三方插件目录>/<版本或unversioned>/<SHA-256>/`，例如 `dotween-pro/1.0.335/…`、`odin-inspector/3.1.10.0/…`。不再套 external-2636 层。
- 六字段展示信息：每个构建的 `plugin.json`。
- 资源路径、GUID、哈希与结构异常：每个构建的 `manifest.json`。
- 权威索引：`index.json` 内的 `external_catalogs["2636"]`，保存全部插件版本、源路径、Git blob、commit、制品哈希和元数据来源。
- 便捷版本索引：`external-2636-index.json`，是权威索引的派生副本。

带空格的版本使用安全目录名并附短哈希避免碰撞，JSON 中的原始版本标签不变。这批插件使用独立 ID 和目录，不与之前 MagicTavern 工程包中的同名/同版本插件自动视为同一发布构建；源仓库也不能证明其内容完全等同官方版本。

元数据优先读取版本目录 package.json；不存在时才读取插件根 package.json，与 MTP 处理方式一致。版本始终取版本索引，公共元数据不覆盖该版本标签。description、unity、published_at 只使用源中提供的字符串；未提供则留空，不把 Git commit 时间当作插件发布时间。

## 下载与校验

下载缓存位于 ServiceBase 的 `.runtime/plugin-downloads-2636`。完成文件使用 Git blob 哈希验证，再复用缓存；未完成的 .part 文件不会入库，下次从该文件开头重下。入库前检查 SHA-256 和归档资源清单，完成整个清单校验后一次提交权威索引。

制品解析不解压到 Unity，不执行插件脚本，不引用 MTP 程序集。结构异常会进入 manifest 的审查项或阻止索引提交；所有条目均为 `execution_ready=false`，下载入库不等于安装验证。

部分 Asset Store 制品使用扩展 gzip 头，读取器通过 gzip 层处理后再解析 tar，并验证 gzip CRC。归档中的商店图标和 `packagemanagermanifest` 单独记录；后者可能涉及 UPM 依赖变化，不作为普通 Assets 资源处理。包含控制字符的原始 pathname 也单独标记，保持原制品不变，不自动修正为可执行路径。

## 更新与检查

在 ServiceBase 目录执行：

```sh
.venv/bin/python -u manage.py plugin_external --list external/external-2636-request.json
.venv/bin/python manage.py plugin_repository verify
.venv/bin/python manage.py test dashboard.test_plugin_external
```

可用 `--root` 和 `--cache` 指定目录。工具沿用已有服务器 GitLab 连接配置，凭据不写入索引。二进制文件不进入普通源码 Git/Docker 构建；备份和部署需包含仓库下全部插件目录及其索引。来源 ID 仍保留以兼容旧记录，不再决定制品目录名。
