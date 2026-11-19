# ServiceBase

Unity 工程与 MTP 包信息工作台。当前支持工程在线状态、完整包目录、工程依赖树、Tag 预览、只读插件仓库标签页，以及向 Unity Console 发送字符串调试消息。

插件仓库位于首页「插件仓库」标签页（`/#plugins`），无需连接 Unity，支持分类、搜索、版本切换和来源详情。接口及配置见 [开发文档](docs/development.md)。

工程选择区新增「单包 UPM 安装」，连接独立执行器后可提交固定 Registry 版本或 Git commit，并查看持久化结果。需先应用数据库迁移并部署 `unity/UpmExecutor/`；[代码设计、部署与验证](docs/single-upm-installation.md)。

**当前外部试用方式：Mac + Docker + 临时 HTTPS 隧道，不需要购买域名。** 工程通过 Unity Bridge 主动连接服务器，不需要给同事的电脑开放端口。包目录已支持依赖与插件预览、多步骤安装、更新和卸载；插件仓库支持独立导入、更新与删除。卸载和更新需要 v8 独立执行器，见 [第 9 步说明](docs/package-maintenance.md)。

## 先看这里：接入一个工程需要什么

管理员给每位同事准备以下内容：

- 当前服务的 **HTTPS 地址**。
- 该同事的**网页登录账号和密码**。
- 该电脑的 **ServiceBase 设备 token**。
- 本仓库 [unity/](unity/) 下的完整脚本目录（**包含 `UpmExecutor/` 和 asmdef**）。

网页登录密码、ServiceBase 设备 token、GitLab private token 是三种不同凭据。**同事接入工程使用设备 token，不使用 GitLab private token。** GitLab 凭据由管理员在服务器设置中维护。

下面的终端命令均在服务器 Mac 的 ServiceBase 仓库根目录执行。普通成员只需完成第 2、3、4 节。

## 1. 管理员：准备地址、账号和设备

### 1.1 获取当前服务地址

确认 Docker Desktop 已启动，查看服务状态：

```sh
.venv/bin/python deploy/quick/quickctl.py status
cat .runtime/quick-url.txt
```

把输出的 HTTPS 地址发给同事。临时地址会变化，不要从旧聊天记录中复制地址。

如果服务尚未启动：

```sh
# 仅首次部署需要 init；不要为了添加工程重复初始化。
.venv/bin/python deploy/quick/quickctl.py init
.venv/bin/python deploy/quick/quickctl.py start
```

`start` 会创建新的临时隧道并同步域名白名单。若已在 Docker Desktop 中重启了 tunnel，使用下面的命令同步现有地址，不会再次创建隧道：

```sh
.venv/bin/python deploy/quick/quickctl.py sync
```

更新现有 Docker 服务到当前代码（保留账号和数据库）：

```sh
.venv/bin/python deploy/quick/quickctl.py update
```

该命令自动备份数据库、更新迁移和网页静态资源，并进行部署检查。更新后保持原来的启停状态，保留正在运行的隧道；需要启动已停止服务时，再执行 `start`。插件仓库完整挂载 `external/`，导入缓存使用独立持久卷。详见 [Docker 更新说明](deploy/quick/README.md#更新当前服务)。

### 1.2 创建同事账号和设备 token

例如，为成员 `alice` 的电脑 `alice-mac` 创建凭据：

```sh
.venv/bin/python deploy/quick/quickctl.py member alice alice-mac
```

终端会输出一个 `.runtime/quick-credentials/member-*.json` 文件路径。文件包含：

- `username`：网页登录账号。
- `password`：首次创建账号时生成的网页登录密码。
- `device`：本机设备名称。
- `device_token`：填写到 Unity 和网页设备绑定页面的 token。

只把这个成员自己的凭据单独交给他。**不要分发 `.runtime/quick-credentials/admin.json`。**

同一成员增加第二台电脑时，保持账号名不变，使用新的设备名称：

```sh
.venv/bin/python deploy/quick/quickctl.py member alice alice-windows
```

已有账号的密码不会改变，新文件中的“已有账号，密码未修改”是说明，不是登录密码。一个账号下不能重复创建相同设备名称。

**同一台电脑的多个 Unity 工程可共用一个设备 token；不同电脑应使用不同 token。** 管理员自己的 Unity 工程也需要设备凭据；可为日常工程使用单独的普通成员账号，网页登录仍可用管理员账号查看全部工程。

### 1.3 管理员登录和 GitLab 配置

临时部署首次初始化会创建 `admin`，随机密码保存在服务器本机 `.runtime/quick-credentials/admin.json`。管理员登录后点击右上角“设置”，配置 GitLab 地址、目录分支、private token 和黑名单工程 ID。

已有配置无需重新填写。token 输入框留空会保留原值，不显示已保存的原文。需要更新完整目录时，回到工程页面点击“同步包信息”。

## 2. 同事：把 Bridge 放进 Unity 工程

1. 将 [unity/](unity/) 下完整目录（**包含 `UpmExecutor/` 和 asmdef**）复制到目标工程：

   ```text
   你的Unity工程/
   └── Assets/
       └── Editor/
           └── ServiceBase/
               ├── ServiceBaseBridge.cs
               ├── BridgeSettings.cs
               ├── BridgePullClient.cs
               ├── 其余兼容脚本
               └── UpmExecutor/ （全部脚本及 ServiceBase.UpmExecutor.asmdef）
   ```

2. 等待 Unity 导入并完成编译。Console 不应出现 Bridge 相关编译错误。
3. 如果工程以前装过 Bridge，替换旧脚本，并移除旧位置的重复副本；不要同时保留两个同名 `ServiceBaseBridge` 类。

**不需要修改 MTP 业务代码，也不需要通过 UPM 安装 Bridge。** 当前仍是 Editor 脚本集合，不能只复制入口文件。文件分工和 MTP 版本适配见 [Unity Bridge 说明](unity/README.md)。

## 3. 同事：配置 Unity 的连接

1. 打开菜单 **Tools → ServiceBase → Settings**。
2. 在“服务器 URL”填写管理员提供的完整 HTTPS 地址，只填站点根地址，不附加 `/accounts/login/` 或 `/api/`。
3. 在“设备 Token”填写本机的 `device_token`。
4. 点击 **“保存并重新连接”**。
5. 若之前手动停止过 Bridge，也可通过 **Tools → ServiceBase → Start** 启动。

外部试用不要填写 `localhost`、`127.0.0.1` 或服务器的局域网 IP；这些地址不能替代临时 HTTPS 地址。

连接设置保存在本机 Unity EditorPrefs，不写入工程 Git。同一操作系统用户下的多个工程共享此设置；已打开的每个工程都应执行一次“保存并重新连接”，使其正在运行的连接切换到新地址。

如果设置了环境变量 `SERVICEBASE_URL` 或 `SERVICEBASE_TOKEN`，它们会覆盖窗口中保存的值。修改窗口后仍连接旧地址时，请检查这两个环境变量，并在修改后重启 Unity。

## 4. 同事：网页登录并绑定本机设备

1. 在浏览器打开管理员提供的同一个 HTTPS 地址。
2. 使用自己的 `username` 和网页登录密码登录。入口文字即使显示“管理员登录”，普通成员账号也使用该登录页面。
3. 点击右上角 **“绑定本机设备”**。
4. 输入刚才填入 Unity 的**同一个设备 token**。
5. 点击 **“绑定并查看工程”**。

普通成员仅登录账号、尚未绑定设备时，看不到工程是正常现象。

权限规则：

- **管理员**：可以查看所有账号连接过的工程，包括离线历史；手动移除的记录仍遵守移除/恢复规则。
- **普通成员**：只能查看当前账号下、当前浏览器会话所绑定设备的工程。
- 同一个成员账号在两台电脑上登录，需要分别绑定各自的设备 token。
- 退出网页登录后需要重新绑定。设备被撤销后，成员也会失去对应工程的访问权限。

这里的“本机”按设备凭据识别，不是硬件认证。不要跨电脑复用或分享设备 token，否则会共享该设备的工程访问权限。

## 5. 验证连接成功

1. 工程列表出现目标工程，显示工程名、路径、设备名和“在线”。正常情况下等待一至两个轮询周期；失败重试期间可能更久。
2. 点击工程卡片，查看包信息。Unity 正在编译或刷新资源时，MTP 状态可能暂时显示初始化中。
3. 点击“消息调试”，输入字符串并发送。
4. Unity Console 应出现对应日志，网页显示 mock 反馈。
5. 点击“刷新包信息”，可请求 Unity 重新采集工程快照。

工程连接正常与 MTP 接入正常是两件事。即使 MTP 未安装或不兼容，Bridge 仍可注册工程并处理字符串调试消息。

## 6. 日常使用与地址变化

### 包信息页面

- **全部包列表**：来自 GitLab 647 仓库的目录缓存，叠加当前工程的安装状态。647 是仓库 ID，不是固定包数量。
- **工程依赖树**：展示工程已有依赖关系，可展开、收起；保留共享依赖、循环和缺失节点提示。
- 排序：已安装 root → 已安装 dep → 未安装，各组按 DisplayName 升序。
- Tag 下拉框用于预览不同版本的信息、依赖和更新日志，不会直接更改工程。
- 选择发布 Tag 预览安装或更新；根包可预览卸载，自动列出保留的共享依赖及可清理项。卸载针对实际安装版本，MTP 自身、本地/嵌入包和仍有引用的包不能由此入口删除。
- 黑名单隐藏未安装的指定 GitLab 工程包，保留已安装包；管理员 Debug 模式可显示全部，工程依赖树保持完整。

### 临时域名改变或出现 Bad Request

如果直接重启 Docker 中的 tunnel，它可能获得新域名，而应用仍只允许旧域名，从而返回 HTTP 400。

管理员执行：

```sh
.venv/bin/python deploy/quick/quickctl.py sync
cat .runtime/quick-url.txt
```

把新地址重新发给同事。每位同事需要：

1. 在 Unity Settings 中更换服务器 URL，点击“保存并重新连接”。
2. 浏览器打开新地址，重新登录并绑定本机设备。

**域名变化不要求重新创建账号或设备 token。** 原数据库和凭据未改变时继续使用原值。

### 停止或恢复服务

```sh
.venv/bin/python deploy/quick/quickctl.py stop
.venv/bin/python deploy/quick/quickctl.py start
```

停止不会删除数据库。Mac 需要保持联网、不休眠，Docker Desktop 需要运行。服务停止或 Unity 关闭后，工程会在心跳过期后显示离线；服务重启不会使工程 ID 失效。

## 7. 常见问题

- **登录后列表为空**：先检查是否绑定设备、账号是否与设备凭据归属一致，再检查 Unity 使用的地址和 token。管理员无需绑定设备。
- **Unity Console 提示“设备 Token 无效”**：确认填写的是 ServiceBase 设备 token，而不是网页登录密码或 GitLab token；确认设备未被撤销。
- **工程提示“已归属其他凭据”**：同一工程记录不允许用新 token 静默接管。先恢复原设备 token，或由管理员处理归属，不能仅靠重新生成 token 修复。
- **找不到 Tools → ServiceBase 菜单**：检查 Bridge 和 UpmExecutor 脚本、asmdef 是否齐全、是否在 Editor 目录，以及 Console 是否有编译错误或重复类。
- **工程在线，但包信息未更新**：“刷新包信息”请求 Unity 快照；管理员“同步包信息”更新服务器 GitLab 目录，两者作用不同。
- **旧 MTP 缓存缺失或损坏**：在 MTP 自身确认依赖缓存可用，再刷新快照；Bridge 不负责修复旧业务缓存。已安装状态的判断依据显示在包详情中。
- **原本地工程在临时服务中看不到**：临时 Docker 服务使用独立数据库，不自动迁移本地 SQLite 的工程记录。工程按第 3 节连接新服务后会重新注册。

## 其他文档

- [当前已实现业务逻辑与能力边界（v10）](docs/current-business-logic.md)
- [Unity Bridge 安装、版本兼容和脚本分工](unity/README.md)
- [临时 HTTPS 服务运维](deploy/quick/README.md)
- [Mac 固定域名隧道及常驻配置](deploy/macos/README.md)
- [通用生产部署](deploy/README.md)
- [本地开发、接口协议与验证边界](docs/development.md)

## v9：独立状态与可选迁移

Bridge 已停用 MTP 读取。Assets 本地包由独立执行器识别；旧记录须在网页主动读取、逐项选择并确认后才迁移。服务端需应用迁移 0013，详见 [独立识别与迁移](docs/legacy-migration.md)。
