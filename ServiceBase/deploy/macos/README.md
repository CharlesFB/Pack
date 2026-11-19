# Mac 对外服务（生产模式）

本配置使用 Docker Desktop + Cloudflare Tunnel。无需公网 IP/路由器入站端口，
域名通过 HTTPS 进入 Tunnel → proxy:8080 → Gunicorn；PostgreSQL、Redis 和代理均不发布宿主机端口。
不要转发本地 runserver，也不要给这套配置添加 SERVICEBASE_MODE=local。

## 1. 准备

- 安装并启动 Docker Desktop（包含 Docker Compose）。设置登录时自动启动 Docker Desktop。
- 在 Cloudflare 托管的域名下选择一个子域名，例如 service.example.com。
- 在 Cloudflare 创建 remotely-managed Tunnel，把 token 单独保存到本工程 `.runtime/tunnel-token`。
  不要把 token 放进命令行、Git 或网页 URL。该文件应限制为本机账号可读。
- 为 Tunnel 添加 Published application route：Hostname 与 DOMAIN 相同，Service URL 为 `http://proxy:8080`。
- 在 Cloudflare 为该域名启用 Always Use HTTPS，确认 HTTP 请求在边缘重定向。
  内部代理明确设定 X-Forwarded-Proto=https，所以只能放在 HTTPS 隧道入口后，不能直接暴露。
- 如果启用 Cloudflare Access，必须另行处理 Unity 原生 API 的服务认证；当前 Bridge 不会完成 Access 网页登录。
  本配置直接使用 Django 登录和设备 Token 鉴权。

官方设置参考：
https://developers.cloudflare.com/tunnel/setup/
https://developers.cloudflare.com/tunnel/advanced/run-parameters/

## 2. 初始化

在工程根目录执行：

```sh
cp .env.example .env
# 编辑 .env：填写 DOMAIN，使用独立随机 SECRET_KEY 和 POSTGRES_PASSWORD。
# .runtime/tunnel-token 填入 Tunnel token 后：
chmod 600 .env .runtime/tunnel-token
deploy/macos/servicectl init
deploy/macos/servicectl admin
```

`init` 构建生产镜像、启动数据库与 Redis、执行迁移和静态文件收集，不启动公网 Tunnel。
管理员通过网页设置 GitLab 地址、token、黑名单。云端目录同步不依赖 Unity 的本地配置路径。
本配置是独立 PostgreSQL 数据库，不自动搬迁本地 SQLite 里的账号、设置和工程记录。

插件仓库 `../../external` 以只读方式挂载到 web 的 `/app/external`，需包含索引、分类目录和全部制品；不打进镜像。导入缓存独立保存在 `plugin_deliveries` 数据卷，可在重建容器后继续使用。数据库、仓库和此缓存卷需一起备份。

更新已有代码时先执行 `servicectl backup`，再执行 `servicectl init` 和 `servicectl start`。`init` 会重新构建镜像、迁移数据库和收集静态资源，原账号保留。

## 3. 成员与设备

在服务器创建成员账号（普通账号，不设 staff），为每台电脑签发独立设备 Token。
可在容器内用 Django shell 的交互 getpass 创建成员，避免把密码写进 shell 历史：

```sh
docker compose --env-file .env -f deploy/macos/compose.yaml run --rm web python manage.py shell
```

```python
from django.contrib.auth import get_user_model
from getpass import getpass
get_user_model().objects.create_user(input('Username: '), password=getpass('Password: '))
```

签发设备凭据：

```sh
docker compose --env-file .env -f deploy/macos/compose.yaml run --rm web python manage.py create_bridge_token MEMBER MAC_NAME
```

该电脑 Unity 中 `Tools/ServiceBase/Settings` 填入 `https://DOMAIN` 和设备 Token。
成员浏览器登录后，点击“绑定本机设备”，输入同一个 Token；会话中只保存凭据 ID，不保存 Token 原文。
一个账号可有多个设备，不同浏览器会话分别绑定设备；退出登录后重新绑定。
管理员（staff）直接看到各账号已连接的工程，包括离线历史；此前主动移除的记录仍遵守移除/恢复规则。
所有工程 API 都执行同样的权限过滤，不能通过手工填写工程 ID 越权。
撤销设备 Token 后对应成员浏览器立即失去工程访问权限；Unity 认证缓存最多 30 秒失效。

这里实现的是账号 + 设备凭据隔离，不是物理硬件证明。不得在多台电脑复用或分享设备 Token；
知道同账号另一台设备 Token 的人可以绑定那台设备。如需强制验证浏览器和 Unity 位于同一物理电脑，
还需本地配对代理/设备证书，当前配置不声称具备该能力。

## 4. 启停与常驻

```sh
deploy/macos/servicectl start
deploy/macos/servicectl status
deploy/macos/servicectl logs
deploy/macos/servicectl stop
```

容器采用 unless-stopped，异常退出会自动重启；手动 stop 后不会被防睡眠任务重新启动。
Docker Desktop 登录启动后可恢复此前未手动停止的容器。Mac FileVault 开机解锁、用户登录前不保证服务可用。
不要将 Mac 合盖当作无人值守运行方式。

可选防睡眠任务：复制 `com.servicebase.keep-awake.plist.example`，把
`REPLACE_WITH_ABSOLUTE_PROJECT_PATH` 替换为工程绝对路径，保存为
`~/Library/LaunchAgents/com.servicebase.keep-awake.plist`，然后执行：

```sh
launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/com.servicebase.keep-awake.plist
# 卸载：
launchctl bootout gui/$(id -u) ~/Library/LaunchAgents/com.servicebase.keep-awake.plist
```

它仅在 web 容器运行时用 caffeinate -s 阻止接电期间的系统空闲睡眠，停止服务后自动结束。
这不阻止主动关机、主动睡眠或断网。模板尚未安装到系统。

## 5. 备份与恢复

```sh
deploy/macos/servicectl backup
```

备份在 `.runtime/backups/`，使用 pg_dump 自定义格式；请定期复制到另一台机器或备份服务。
另行安全备份 `.env`，数据库包含 GitLab 凭据。maintenance 容器每天清理过期任务，不负责数据库备份。

恢复应先停止 web/tunnel/maintenance，再向新建的空 PostgreSQL 数据库导入备份：

```sh
docker compose --env-file .env -f deploy/macos/compose.yaml exec -T db pg_restore -U servicebase -d NEW_EMPTY_DATABASE --no-owner < BACKUP.dump
```

在隔离数据库完成恢复演练后再切换服务连接，不要直接覆盖运行中的数据库。

## 验收

- 使用外部网络测试 HTTPS 网页、管理员登录、成员设备绑定、Unity 注册与消息任务。
- 以同一账号的两台设备测试列表、快照、任务 API 隔离。
- 重启容器后验证快照和管理员设置保留。
- 确认 Mac 能访问公司 GitLab；VPN 中断会影响目录同步。
- 已在临时 Quick Tunnel 部署验证 Docker、PostgreSQL/Redis 和 HTTPS 登录、设备任务；本文固定域名隧道仍需按所选域名独立验收。临时试用见 [Quick Tunnel 运维](../quick/README.md)。

## v8 安装管理升级

升级服务前先停止 web 和维护任务写入，备份数据库，随后构建镜像、应用全部迁移（当前至 0012）并收集静态资源；不要让旧服务在迁移期间继续处理任务。服务启动前，可在 web 容器运行 `python manage.py check_installation_deployment`，验证 PostgreSQL、Redis、插件仓库、缓存目录、前端静态模块和执行器文件。更新镜像后仍需在 Unity 工程整体部署 v8 `UpmExecutor/`。具体操作与恢复边界见 [包维护文档](../../docs/package-maintenance.md)。
