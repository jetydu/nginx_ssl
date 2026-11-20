# Nginx SSL Manager

基于 **.NET 10 + Vue 3** 的 Nginx SSL 证书管理控制台：

- 自动扫描 nginx 配置，识别并纳管已有证书
- 监控证书到期时间，到期前自动通过 **Let's Encrypt（ACME v2 / HTTP-01）** 续期
- 续期后写回证书文件，校验 `nginx -t` 并平滑重载
- 修改 nginx 配置前自动备份，校验失败自动回滚
- Web 控制台 + Scalar API 文档，单进程同源部署

---

## 功能概览

| 模块 | 说明 |
| --- | --- |
| 证书总览 | 证书总数、即将到期、异常、自动续期统计，证书状态分布与操作动态 |
| 证书管理 | 新建 / 编辑 / 立即续期 / 同步元数据 / 部署到 nginx / 删除记录 |
| Nginx 站点 | 解析 `nginx.conf` 及 `include` 文件，列出 server 块、监听端口、SSL 证书 |
| 操作日志 | 按级别、类型、域名筛选，支持自动刷新与清空 |
| 系统设置 | ACME 目录、续期策略、证书目录、Webroot、nginx 路径与管理员密码 |

后台任务默认每 **60 分钟**检查一次：自动扫描导入、同步证书状态、对进入续期窗口的证书自动续签。

---

## 技术栈

- 后端：ASP.NET Core (.NET 10)、EF Core + SQLite、Certes（ACME v2）、JWT
- 前端：Vue 3、Vite、TypeScript、Element Plus、Pinia、Vue Router、ECharts
- API 文档：Microsoft.AspNetCore.OpenApi + Scalar

---

## 目录结构

```
.
├── NginxSslManager.slnx
├── README.md
├── deploy/                       # systemd / nginx 示例配置
├── scripts/                      # 构建发布脚本
├── src/NginxSslManager.Api/      # 后端项目（wwwroot 为前端构建产物）
│   ├── Background/               # 自动续期后台任务
│   ├── Controllers/              # REST API
│   ├── Services/                 # ACME、证书管理、nginx 解析与部署
│   └── data/                     # 运行时数据（SQLite / JWT / ACME 账户 / 备份）
├── tests/NginxSslManager.Tests/
└── web/                          # Vue 前端源码
```

---

## 快速开始

### 1. 环境要求

- [.NET SDK 10](https://dotnet.microsoft.com/)（验证版本：`10.0.404`）
- [Node.js 22+](https://nodejs.org/)（验证版本：`v22.22.0`）
- 已安装并可运行的 nginx（默认路径 `/usr/sbin/nginx`、`/etc/nginx/nginx.conf`）

### 2. 构建前端

```bash
cd web
npm install
npm run build
```

构建产物会直接输出到 `src/NginxSslManager.Api/wwwroot`，由后端同源托管。

### 3. 启动后端

```bash
cd src/NginxSslManager.Api
dotnet run
```

默认监听 `http://0.0.0.0:5188`：

- 控制台：<http://127.0.0.1:5188/>
- API 文档（Scalar）：<http://127.0.0.1:5188/scalar>
- OpenAPI JSON：<http://127.0.0.1:5188/openapi/v1.json>
- 健康检查：<http://127.0.0.1:5188/api/health>

> 修改监听地址：编辑 `appsettings.json` 的 `Urls`，或启动时追加参数 `--urls http://0.0.0.0:5188`（优先级更高，systemd 示例即使用此方式）。

### 4. 运行测试（可选）

```bash
dotnet test
```

目前覆盖 nginx 配置解析、令牌化与证书状态计算。

### 5. 前端开发模式（可选）

```bash
# 终端 1：后端
cd src/NginxSslManager.Api && dotnet run

# 终端 2：前端（http://127.0.0.1:5173，自动代理 /api 到 5188）
cd web && npm run dev
```

---

## 首次登录

| 项目 | 默认值 |
| --- | --- |
| 用户名 | `admin` |
| 密码 | `admin123` |

首次登录后控制台会持续提示修改密码，请在 **系统设置 → 管理员安全** 中立即修改。

- 密码使用 PBKDF2 加盐哈希存储
- JWT 签名密钥首次启动时自动生成到 `data/jwt.key`
- 登录接口按来源 IP 限流（每分钟 10 次）

---

## 数据目录

默认数据目录为项目的 `data/`，可通过 `appsettings.json` 或环境变量修改：

```json
{
  "Storage": { "DataDirectory": "data" }
}
```

```bash
Storage__DataDirectory=/var/lib/nginx-ssl-manager
```

目录内容：

| 路径 | 说明 |
| --- | --- |
| `data/nginx-ssl-manager.db` | SQLite 数据库（证书、设置、日志、管理员） |
| `data/jwt.key` | 自动生成的 JWT 签名密钥 |
| `data/acme/` | ACME 账户私钥与注册信息（按目录地址区分） |
| `data/backups/` | 修改 nginx 配置前的备份文件（`*.bak`） |

> 备份数据库即可完整迁移；`jwt.key` 与 `acme/` 属于敏感凭据，注意文件权限。

---

## 使用流程

### 导入现有证书

进入 **证书管理 → 从 Nginx 导入**（或 **Nginx 站点 → 导入证书**）。系统会：

1. 解析 nginx 配置中的 `ssl_certificate` 路径
2. 读取证书域名、颁发者、有效期、序列号与指纹
3. 创建只读监控记录（`autoRenew = false`），不会覆盖原证书

### 新建并签发证书

**证书管理 → 新建证书**，填写域名（每行一个），选择是否创建后立即申请：

- 域名必须已解析到本机，且公网可访问 **80 端口**（HTTP-01 要求）
- 不支持通配符域名（需 DNS-01）
- 签发成功后证书默认写入：
  - `/etc/nginx/ssl/letsencrypt/<主域名>/fullchain.pem`
  - `/etc/nginx/ssl/letsencrypt/<主域名>/privkey.pem`
- 可在高级设置中自定义 ACME 目录（支持 Let's Encrypt Staging）与证书路径

### 部署到 Nginx

点击 **部署** 后，系统会：

1. 找到 `server_name` 匹配的 server 块
2. 备份原配置文件到 `data/backups/`
3. 写入 `ssl_certificate` / `ssl_certificate_key`
4. 执行 `nginx -t` 校验，失败则回滚
5. 平滑重载 nginx（`nginx -s reload`），失败同样回滚

### 自动续期

后台任务按设定间隔执行：

1. 自动扫描 nginx 并导入未纳管证书（可关闭）
2. 同步所有证书的到期时间与状态
3. 对 `autoRenew = true` 且进入续期窗口（默认提前 30 天）的证书自动续签
4. 签发成功后按证书的 `autoDeploy` 设置更新 nginx 配置并重载

---

## HTTP-01 验证模式

在 **系统设置 → 证书与验证路径** 中配置，`challenge.mode` 支持：

| 模式 | 说明 |
| --- | --- |
| `auto`（默认） | 申请时临时向匹配的 80 端口 server 块注入 `/.well-known/acme-challenge/` location，验证完成后立即恢复原配置 |
| `webroot` | 仅把验证文件写入固定 Webroot，要求站点已自行配置 `/.well-known/acme-challenge/` 指向该目录 |

`auto` 模式需要：

- 域名 A/AAAA 记录指向本机
- 公网可访问 80 端口（或 80 端口验证请求能回源）
- 进程对 `/etc/nginx` 有写权限，并可执行 `nginx -t` / `nginx -s reload`

---

## API 文档（Scalar）

启动后访问 <http://127.0.0.1:5188/scalar>。

1. 先调用 `POST /api/auth/login` 获取 JWT
2. 点击右上角 **Authorize**，填入 Token（无需手动加 `Bearer ` 前缀）
3. 其余受保护接口即可直接调试

OpenAPI JSON 位于 `/openapi/v1.json`，可用于生成客户端或接入其他工具。

---

## 生产部署（systemd）

### 1. 发布

```bash
./scripts/publish.sh          # 输出到 ./publish
# 或手动：
dotnet publish src/NginxSslManager.Api/NginxSslManager.Api.csproj -c Release -o publish
```

发布目录已包含前端静态文件，无需额外部署 Vue 服务。

### 2. 安装

```bash
sudo mkdir -p /opt/nginx-ssl-manager /var/lib/nginx-ssl-manager
sudo cp -r publish/* /opt/nginx-ssl-manager/
sudo cp deploy/nginx-ssl-manager.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now nginx-ssl-manager
```

### 3. 服务文件

`deploy/nginx-ssl-manager.service` 默认以 **root** 运行，因为系统需要：

- 写入 `/etc/nginx` 与证书目录
- 执行 `nginx -t` / `nginx -s reload`

如需降权运行，请改用 `nginx` 用户并配置 sudoers 或写入权限，同时确保相关目录可写。

常用命令：

```bash
systemctl status nginx-ssl-manager
journalctl -u nginx-ssl-manager -f
```

### 4. 反向代理（可选）

控制台默认只监听 `0.0.0.0:5188`。如需通过域名 + HTTPS 暴露，可参考 `deploy/nginx-site.conf.example`。

> 首次为控制台域名申请证书时，可先临时通过 HTTP 访问，签发成功后再启用 HTTPS。

---

## 配置项说明

`appsettings.json` 中的 `Storage` / `Jwt` / `Urls` 属于进程级配置；业务配置保存在数据库中，通过控制台或 `PUT /api/settings` 修改。

| 设置键 | 默认值 | 说明 |
| --- | --- | --- |
| `acme.directory` | Let's Encrypt 生产 | ACME v2 目录 |
| `acme.stagingDirectory` | Let's Encrypt Staging | 测试目录 |
| `acme.email` | 空 | 默认联系邮箱 |
| `renew.daysBefore` | `30` | 提前续期天数（1-120） |
| `renew.checkIntervalMinutes` | `60` | 检查间隔（5-10080 分钟） |
| `renew.autoRenewEnabled` | `true` | 全局自动续期开关 |
| `nginx.binaryPath` | `/usr/sbin/nginx` | nginx 可执行文件 |
| `nginx.configPath` | `/etc/nginx/nginx.conf` | 主配置文件 |
| `nginx.prefix` | `/etc/nginx` | nginx prefix |
| `nginx.reloadCommand` | `/usr/sbin/nginx -s reload` | 重载命令，支持 `{binary}` / `{config}` 占位符 |
| `nginx.testEnabled` | `true` | 重载前执行 `nginx -t` |
| `nginx.autoImport` | `true` | 自动扫描导入 nginx 证书 |
| `webroot.default` | `/var/www/letsencrypt` | 默认 Webroot |
| `paths.certificateRoot` | `/etc/nginx/ssl/letsencrypt` | 新证书默认根目录 |
| `challenge.mode` | `auto` | `auto` 或 `webroot` |

---

## 常见问题

**提示 nginx 配置检查失败？**

页面会展示 `nginx -t` 的完整输出。修改 nginx 配置的操作都会先备份到 `data/backups/`，失败自动回滚；也可手动复制备份文件恢复。

**申请证书失败？**

依次确认：域名解析、80 端口公网可达、ACME 邮箱/目录正确、服务器时间准确。可先在设置中切换 Let's Encrypt **Staging** 调试。

**控制台提示未构建前端？**

在 `web/` 下执行 `npm run build`，确认产物写入了 `src/NginxSslManager.Api/wwwroot/index.html`。

**忘记管理员密码？**

停止服务后删除数据库中的 `admin.passwordHash` 设置项（或删除整个数据库重新初始化），再次启动会恢复默认密码 `admin123`。

**重置系统？**

停止服务，删除 `data/nginx-ssl-manager.db*` 后重启即可重建数据库（不会删除磁盘上的证书和 nginx 配置）。

---

## 已知限制

- 仅支持 **HTTP-01**，不支持通配符证书（`*.example.com`，需要 DNS-01）
- 申请证书要求 80 端口公网可达，CDN/反代场景需确保验证请求回源
- 自动验证依赖对 `/etc/nginx` 的写权限，建议以 root 或具备同等权限的账户运行
- 本机无法完成真实公网签发测试时，可先使用 Let's Encrypt Staging 调试

## 安全建议

- 首次登录后立即修改默认密码
- 生产环境建议通过 HTTPS 反向代理暴露控制台，并限制来源 IP
- 保护好 `data/jwt.key`、`data/acme/` 与私钥文件（`privkey.pem`），权限建议 `600`
- 定期备份 `data/` 目录
