# 生产部署

本文针对根目录 `compose.yaml` 的固定域名直连部署。当前无域名临时试用见 [Quick Tunnel 运维](quick/README.md)。固定域名部署需要服务器已安装 Docker Compose，并有指向该服务器的域名。Caddy 管理 HTTPS；仅代理开放 80/443，数据库、Redis 和 Gunicorn 都不映射宿主机端口。不得把 Gunicorn 直接暴露给外部，否则应用信任的 X-Forwarded-Proto 头失去可信来源。

1. 复制 `.env.example` 为 `.env`，填写 DOMAIN、随机 SECRET_KEY 和 POSTGRES_PASSWORD（不要提交 Git）。
2. `docker compose build`
3. `docker compose up -d db redis`
4. `docker compose run --rm web python manage.py migrate`
5. `docker compose run --rm web python manage.py collectstatic --noinput`
6. `docker compose run --rm web python manage.py createsuperuser`
7. `docker compose run --rm web python manage.py create_bridge_token <用户名> <设备名称>`
8. `docker compose up -d web proxy`

插件仓库需要完整保留在宿主机 `external/` 下，通过只读挂载提供给 web，未包含在镜像中。导入制品缓存保存在 `plugin_deliveries` 数据卷。容器以 UID 10001 执行，镜像为新缓存卷准备可写目录。自定义部署可配置 `PLUGIN_REPOSITORY_ROOT` / `PLUGIN_DELIVERY_ROOT`，并同步对应卷挂载。重建容器前后均需保留数据库、仓库和导入缓存。

代码更新：先备份数据库，再依次执行 build、migrate、collectstatic 和 `docker compose up -d web proxy`；只重启旧容器不会更新镜像或静态文件。不要在更新时使用 `down -v`。

令牌只输出一次；填入该设备 Unity 的 Tools/ServiceBase/Settings，URL 为 https://你的域名。管理员（staff）可查看全部账号的工程；普通成员须在 /accounts/device/ 绑定自己本机的设备 Token，只能查看该账号、该设备的工程。本版没有团队共享和账号管理页面；可通过 Django 管理命令维护账号。

撤销设备：`docker compose exec web python manage.py revoke_bridge_token <凭据ID>`。撤销不会删除历史工程。直接修改数据库 active 字段时，凭据缓存最多延迟 30 秒失效。重新发放的凭据不能静默接管原凭据的工程，需要管理员显式迁移归属或清理原记录。

每天运行 `docker compose exec web python manage.py prune_bridge_tasks` 清理超过 24 小时的过期任务历史；命令已提供，未在本机创建定时任务。备份 PostgreSQL 数据卷和服务 SECRET_KEY；Redis 仅存短期在线状态和认证缓存，丢失后可自动恢复。Redis 重启可能短暂显示离线。离线后列表最近心跳可能回退到最后注册时间，因为持续心跳未写数据库。

默认 Gunicorn 4 workers × 4 threads，用于短请求轮询；这是起始配置，不是 1000 工程容量认证。提供 WSGI 与 ASGI 入口；视图当前仍是同步 ORM/短轮询，切换 ASGI 不会自动变成长连接。上线前在实际配置验证持续负载、登录保护、日志、备份恢复及访问控制；可将服务放在公司 VPN/SSO 网关后面。

最低压测场景：1000 工程约 200 次空闲轮询/秒、集中启动注册、快照同时变化、至少 10 个网页用户、Unity 长时间无响应、Redis/服务重启。分别记录错误率、p95/p99、任务延迟、数据库连接与内存，不能用本机单轮突发结果代替。

生产配置缺少任何必要环境变量会拒绝启动。不要用 LOCAL_MODE、进程内缓存、SQLite 或 runserver 替代生产配置。首次迁移前请备份现有数据库。

生产入口已通过导入和 Django 部署检查；Docker 与公网 HTTPS 联调在 Quick Tunnel 栈完成，固定域名直连部署仍需单独验收。HSTS 未开启子域名覆盖和浏览器预加载；这两项检查提示保留，待域名归属与所有子域的 HTTPS 策略确定后再配置。

Mac 公网隧道与常驻配置见 [macOS 部署](macos/README.md)。

## v8 安装管理升级

升级服务前先停止 web 和维护任务写入，备份数据库，随后构建镜像、应用全部迁移（当前至 0012）并收集静态资源；不要让旧服务在迁移期间继续处理任务。服务启动前，可在 web 容器运行 `python manage.py check_installation_deployment`，验证 PostgreSQL、Redis、插件仓库、缓存目录、前端静态模块和执行器文件。更新镜像后仍需在 Unity 工程整体部署 v8 `UpmExecutor/`。具体操作与恢复边界见 [包维护文档](../docs/package-maintenance.md)。
