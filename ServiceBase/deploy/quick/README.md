# 临时 HTTPS 服务运维

此方式使用 Mac + Docker + Cloudflare Quick Tunnel，无需购买域名。业务运行在生产模式，不开放 Mac 宿主机端口。独立使用 `servicebase-quick` PostgreSQL 数据卷和 `.runtime/quick.env`，不覆盖本机 SQLite 或固定域名部署的数据。

同事连接 Unity、登录和绑定设备的完整步骤见 [主 README](../../README.md)。以下命令均由管理员在 ServiceBase 根目录运行。

## 首次启动

确认 Docker Desktop 已运行、本地 `.venv/bin/python` 可用：

```sh
.venv/bin/python deploy/quick/quickctl.py init
.venv/bin/python deploy/quick/quickctl.py start
```

`init` 构建服务、迁移数据库、收集静态资源，并首次创建管理员 `admin`。随机密码保存在 `.runtime/quick-credentials/admin.json`；已有账号不会重置密码。

`start` 创建新隧道，自动同步应用允许域名和 CSRF 配置，再启动网页服务。输出的 HTTPS 地址也写入 `.runtime/quick-url.txt`。仅允许本次生成的精确域名。

管理员登录后在“设置”配置 GitLab 信息和黑名单，再点击“同步包信息”拉取完整目录。初始化不会自动迁移本地 SQLite 工程记录；Unity 连接新地址后会重新注册。

## 更新当前服务

```sh
.venv/bin/python deploy/quick/quickctl.py update
```

`update` 重建 web / maintenance 镜像，暂时停止原来运行的 web、proxy、maintenance，再启动已有数据库和 Redis。在 `.runtime/backups/` 保存权限为 600 的 PostgreSQL 升级前备份，执行数据库迁移、静态资源收集和部署检查后替换容器。原账号、设备凭据和数据卷保留；不会新建或重启 tunnel。

**更新保留原来的启停状态。** 原本已停止时，更新后 web / proxy / maintenance 保持未启动，临时启动的数据库和 Redis 也会停止；以后从 Docker Desktop 启动容器会使用新镜像。原本运行时，只恢复此前运行的应用服务。迁移或检查失败时，应用保持停止，备份保留，不自动尝试数据库回滚。

需要对外启动时再执行 `start`，它会生成新的临时访问地址。运行中的隧道会在 `update` 期间保持原地址，应用迁移时会有短暂不可用。

插件仓库通过 `../../external:/app/external:ro` 整体挂载，包含分类目录、索引和制品，不再打入镜像。部署前必须准备完整的 `external/`，不能只有 JSON 索引。仓库修改后无需重建镜像；代码和静态界面变更后执行 `update`。

导入缓存使用独立 `plugin_deliveries` 数据卷，路径 `/app/.runtime/plugin-deliveries`，执行器用户可写；重建容器后已派发制品仍可下载。数据库、完整 `external/` 和 `plugin_deliveries` 需要一起备份，存在未结束任务时不要删除缓存卷。

## 添加成员和电脑

```sh
.venv/bin/python deploy/quick/quickctl.py member alice alice-mac
```

命令输出新生成的 `.runtime/quick-credentials/member-*.json` 路径。将其中账号、首次生成的密码、设备 token 与当前 HTTPS 地址单独提供给该成员，不要共享管理员文件。

已有账号密码保持不变；增加电脑时使用同一账号和不同设备名。一台电脑的多个工程可共用设备 token。Unity Settings 填写地址和 token；成员网页登录后还需绑定同一个 token。详见 [接入步骤](../../README.md)。

## 查看、停止与恢复

```sh
.venv/bin/python deploy/quick/quickctl.py status
cat .runtime/quick-url.txt
.venv/bin/python deploy/quick/quickctl.py stop
.venv/bin/python deploy/quick/quickctl.py start
```

按需执行对应命令。`stop` 保留数据库；再次 `start` 通常生成不同地址。隧道容器不随 Docker 自动重启，恢复时运行 `start`。Mac 需保持联网、不休眠，Docker Desktop 需持续运行。

地址改变后，把新地址发给成员：每个已打开的 Unity 工程修改服务器 URL 并重新连接，浏览器打开新地址后重新登录、绑定设备。原数据库未改变时无需重新创建账号或 token。

## 重启 tunnel 后出现 400 Bad Request

直接在 Docker Desktop 重启 tunnel 可能生成新域名，但 Django 仍只允许旧域名。运行：

```sh
.venv/bin/python deploy/quick/quickctl.py sync
cat .runtime/quick-url.txt
```

`sync` 读取当前隧道本次启动的日志，同步域名配置并重建网页服务，不创建新隧道。地址文件表示最近一次成功同步的地址；直接重启 tunnel 后，应先同步再读取它。日常恢复优先使用 `start`，它会自动同步。

## 文件与验证范围

- `.runtime/quick.env`：部署配置，含服务密钥，不分发。
- `.runtime/quick-url.txt`：最近同步的临时 HTTPS 地址，可分享。
- `.runtime/quick-credentials/`：账号与设备凭据文件，只向成员提供其自己的凭据。
- Docker 数据卷：持久化数据库等数据；停止服务不等于删除数据卷。

已完成 Docker PostgreSQL/Redis 和公网 HTTPS 联调，覆盖匿名 API 拒绝、管理员登录、成员设备绑定、Bridge 注册/轮询/任务回执。尚不代表 500～1000 工程容量验证。

此方式适合临时试用，不提供固定地址或生产可用性保证。限制见 [Cloudflare Quick Tunnel 官方说明](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/do-more-with-tunnels/trycloudflare/)。

## 2026-09-21 更新验收

本次 Docker 更新保留原 PostgreSQL 数据和账号，应用 dashboard 0006–0009 迁移，并收集最新静态资源。Linux 镜像内 107 项测试通过；容器内 45 个插件、58 个构建全部可用，仓库挂载只读、导入缓存卷可写。通过实际 HTTPS 地址验证原管理员登录、匿名 API 拒绝、插件内容接口，以及四个关键前端文件与源码一致。临时地址请以 `.runtime/quick-url.txt` 为准。

## v8 安装管理部署（第 7–9 步）

当前镜像包含独立执行器 v8 和迁移至 0012 的全部服务代码。`update` 会同时更新计划预览、多步骤进度、插件更新/删除界面与静态模块。插件仓库和导入缓存继续使用原有挂载及持久卷；任务、计划与各类回执保存在 PostgreSQL。

部署自检可独立执行（需数据库和 Redis 已启动）：

```sh
docker compose --env-file .runtime/quick.env -f deploy/quick/compose.yaml run --rm --no-deps web python manage.py check_installation_deployment
```

检查迁移、关键模型、Redis 读写、仓库制品可用性、导入缓存写入、静态模块与当前源码是否一致，以及 v8 执行器源码是否完整。不会创建安装任务或访问 Unity 工程，不代替每次导入时的完整制品 SHA 校验。

Docker 更新不会把 C# 自动复制到用户工程。包卸载与插件替换需要在 Unity 工程整体更新 `unity/UpmExecutor/` 并保留已有 `.meta`；版本限制、MTP 边界与恢复方式见 [第 9 步说明](../../docs/package-maintenance.md)。

### 2026-09-22 v8 更新验收

已重建 web / maintenance 并替换停止中的容器，现有 PostgreSQL 完成 0010–0012 迁移；账号、设备凭据及原数据卷保留。升级前备份：`.runtime/backups/quick-20260922-133142-aa7402.dump`（权限 600）。部署自检确认 45 个插件、58 个构建、94 条映射及最新静态资源可用。

新镜像内 151 项测试通过；隔离容器中 Gunicorn 启动正常，登录页返回 200、匿名插件接口返回 401、无设备凭据的更新回执接口返回 403。验证未开放宿主机端口。正式服务保持停止，tunnel 未重启；需要访问时由管理员执行 `start`。本次未部署 Unity 工程脚本。
