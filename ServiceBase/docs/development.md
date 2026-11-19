# 本地开发与接口说明

离线插件仓库的构建、校验与当前映射范围见 [插件仓库](../external/README.md)；未来安装器的独立边界见 [MTP 分离记录](mtp-separation.md)。当前没有插件安装接口。

工程接入步骤见 [主 README](../README.md)。本文面向开发和维护人员，不是同事接入远端服务的操作流程。

## 本地开发启动

```sh
uv venv --python 3.12 .venv
uv pip install --python .venv/bin/python -r requirements.txt
.venv/bin/python manage.py migrate
.venv/bin/python manage.py runserver 127.0.0.1:8000
```

开发服务使用 SQLite 和进程内缓存。回环地址的匿名访问仅可见无账号、无设备归属的本地记录；已登录的普通成员仍按设备权限过滤。管理员设置要求登录。不要把本地模式经隧道公开给外部。

临时公网服务使用独立 PostgreSQL/Redis 数据和账号。要管理公网账号，请使用对应 Docker 命令或 quickctl，不要误用本机 SQLite 的 `manage.py createsuperuser`。

## 通信与状态

- 启动时注册；约每 5 秒带抖动轮询，45 秒无心跳判离线。失败时退避重试。
- 快照约每 30 秒采集，仅内容变化时上传；服务端也进行内容去重。手动刷新会创建任务。
- 每个 Unity 客户端仅一个网络请求在途；Unity API 在主线程调用。
- 工程 ID 为设备标识与规范工程路径的 SHA-256；编辑器会话 ID 跨域重载保留，退出 Editor 后更换。
- 删除列表项只隐藏当前会话，可撤销；新编辑器会话可重新出现，不删除工程文件。
- 任务每工程最多 32 条，60 秒过期，轮询每次最多返回 4 条；客户端逐项处理并通过回执确认。
- 网页每 5 秒刷新当前页，默认 50 条，上限 100 条；只读取选中工程的快照，限制并发读取数量。
- 旧 v1 本地转发仅保留开发兼容，生产模式拒绝；外部接入必须使用当前 Bridge 脚本集合。

## 鉴权与接口

原生请求需要 `X-ServiceBase-Client: unity-bridge-v2` 和 `Authorization: Bearer <设备token>`，不接受 Origin 请求头。匿名本地开发可省略 token。设备 token 仅以 SHA-256 摘要存入数据库。

网页使用 Django 登录会话和 CSRF。管理员可查看全部账号工程；普通成员按账号和会话绑定设备过滤所有工程及任务接口。

- `POST /api/bridge/register/`：注册工程和编辑器会话，不允许其他凭据静默接管。
- `POST /api/bridge/poll/`：更新在线缓存并领取当前会话的任务。
- `POST /api/bridge/snapshot/`：上报快照，限制 2 MB、5000 个包。
- `POST /api/bridge/result/`：上报结果，重复回执不覆盖首次完成结果。
- `GET /api/projects/?page=1&page_size=50&q=名称`：分页及名称搜索。
- `GET /api/projects/<id>/mtp/`：返回最近快照并合并服务端目录，包含历史状态提示。
- `POST /api/projects/<id>/mtp/refresh/`：排队或复用快照刷新任务。
- `POST /api/projects/<id>/message/`：发送 GET/POST 语义的字符串 mock 任务。
- `GET /api/tasks/<task_id>/`：返回 pending、done 或 expired 状态。
- `DELETE /api/projects/<id>/`、`POST /api/projects/<id>/restore/`：移除和恢复工程记录。
- `POST /api/catalog/refresh/`：管理员同步 GitLab 目录。
- `/settings/`：管理员设置页面；`/accounts/device/`：成员设备绑定页面。

## 完整目录与凭据

Python 使用 python-gitlab 从 GitLab 647 仓库读取同一 commit 的 `upm-list.json` 与 `upm-cache.json`，持久化允许展示的元数据。心跳不触发目录拉取，同步失败保留上次缓存。

管理员设置优先于环境变量。未设置管理员配置时，可使用 `MTP_GITLAB_URL`、`MTP_GITLAB_TOKEN`、`MTP_GITLAB_REF`；本地还可通过 `MTP_CONFIG_PATH` 或 `.runtime/catalog-source.json` 指向 MTP 配置文件。生产模式不读取 Unity 本地配置路径。

GitLab private token 与设备 token 不同：管理员保存的 GitLab token 在服务数据库中，不回显到前端；数据库和备份应限制读取权限。目录缓存按来源指纹隔离，同一服务的登录用户共享管理员配置的目录。

## 验证

单包 UPM 安装的代码设计、API 状态与部署说明见 [single-upm-installation.md](single-upm-installation.md)。该模块使用独立的持久化安装任务，不复用下面消息调试任务的 60 秒过期机制。

### 插件仓库只读视图（2026-09-15）

工作台顶部「插件仓库」标签页（`/#plugins`）独立于工程连接，支持分类、名称/ID 搜索、分页、版本/构建切换、六项展示信息、制品状态、审查记录和来源包关联。页面不创建 Unity 任务，不调用 MTP 业务。工程导入状态统一为「未检测」。

仓库路径由 `PLUGIN_REPOSITORY_ROOT` 配置，默认 `BASE_DIR/external`。`index.json` 是查询来源，分类按实际顶层目录派生；`groups.json` 为离线工具生成的索引。JSON 按文件更新时间缓存，刷新页面会重新检查文件。此接口只核对元数据身份、制品存在及大小，SHA-256 是入库记录；完整内容校验仍由离线仓库验证命令执行。

所有响应包含 `api_version: 1`，沿用现有会话鉴权：本地回环开发可匿名读取，生产需登录，普通成员可浏览共享仓库。响应不包含服务器绝对路径、原始凭据或下载地址。

- `GET /api/plugins/groups/`：分类及插件、构建总数。
- `GET /api/plugins/?group=&q=&page=1&page_size=30`：分类/名称/ID 筛选，分页最大 100。
- `GET /api/plugins/<plugin_id>/`：全部构建、版本和来源详情。
- `GET /api/plugins/package-mapping/?name=<完整包名>&commit=<40位commit>`：精确包版本映射；未保存返回 404，不回退到其他版本。

只接受 GET；无索引/无效索引返回 503。单条制品缺失或大小异常保留列表条目并标示不可用。未知版本保持空值并在页面显示 `unversioned`；当前索引标记表示仍被当前来源清单引用，不表示最高版本或安装兼容。下载、导入、卸载及更新留待后续执行器阶段。

### 插件内容浏览（2026-09-20）

插件详情新增资源管理器形式的只读目录树，按当前所选版本及构建展示文件夹、文件名和大小。支持多层展开、全部折叠、显示/隐藏 `.meta`；每个目录分批读取，每批最多 100 项。切换构建时取消旧请求，切换目标工程不会重置已展开的资源目录。

- `GET /api/plugins/<plugin_id>/builds/<build_id>/contents/?version=<精确版本或空值>&path=<目录路径或空值>&page=1&meta=0`：返回当前目录的直接子项；目录优先、名称排序。`meta=1` 展示 `.meta`，分页大小固定 100。
- 身份由插件 ID、构建哈希和版本共同限定；路径只查询已索引资源，不能作为服务器文件系统路径读取。沿用共享仓库的登录权限和 `no-store` 响应。
- `unitypackage` 使用 manifest 中的 Unity 资源路径，将配套 `asset.meta` 展示为对应 `.meta` 文件；ZIP 使用归档内原始路径。父目录根据路径补齐，ZIP 空目录和未列入资源索引的归档辅助成员不在此视图中。
- 仓库文件缺失时仍可查看已保存的内容清单；该视图不读取文件正文、不解压制品，也不代表已校验当前 Unity 工程中的文件。
- 新增 `dashboard/plugin_contents.py` 和前端 `plugin-contents.mjs`，不需要 Unity 或数据库升级。

验收：现有仓库 58 个构建的根目录查询全部通过；浏览器验证 Adjust 多层资源目录、大小及 `.meta`，Demigiant Bundle 的 ZIP / unitypackage 构建切换。测试页面使用隔离数据库和临时服务。

### 回归命令

```sh
.venv/bin/python manage.py test dashboard
node --test tests/dependency-graph.test.mjs tests/package-display.test.mjs
```

`tests/run_mtp_smoke.py` 可使用指定 Unity 与本地 MTP 程序集编译 Bridge，验证新旧适配；详见脚本参数。包数量随实际缓存变化，不作为固定验收数字。

已完成 Docker Linux ARM64 镜像内的回归测试，以及 PostgreSQL/Redis + 临时公网 HTTPS 的登录、成员绑定、设备注册和消息回执联调。真实 Unity 也完成过字符串消息及只读快照验证。

`reports/pull_probe.py` 的 1000 工程测试是本地突发视图测试，不等于公网容量认证。500～1000 工程长期负载、网络抖动及多用户访问仍需在目标部署环境压测。
