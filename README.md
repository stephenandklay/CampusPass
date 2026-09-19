# CampusPass

面向 Windows 11 的校园网后台自动认证工具。

它在后台静默完成门户登录，不需要打开浏览器、不需要点任何按钮——连上网络就能上网。
界面是 WPF + Fluent（Mica 云母背景），常驻后台服务的内存占用约 **12 MB**。

[**→ 到 Releases 下载最新版**](releases)

---

## 为什么需要它

很多校园网用 Web 门户认证：插上网线或连上 Wi-Fi 后，必须打开浏览器跳到 `10.x.x.x`
之类的地址、输入账号密码才能上网。这带来两个麻烦：

- 每次重连都要手动登录一遍，开机自启的软件（更新、同步、远程）会在登录前全部失败；
- 认证有超时，掉线后要重新点。

CampusPass 在后台监听网络状态，发现"连上了但上不了网"就自动向门户提交登录表单，
全程无窗口、无浏览器、无命令行黑框。

## 功能特性

- **静默认证**：不打开 Edge/Chrome，不弹终端窗口
- **低资源**：确认联网成功后立即停止轮询，只在网络变化或断网时唤醒；空闲时 CPU 增量为 0
- **表单自动识别**：抓取门户页面里的 `<form>`，自动确定提交地址、账号/密码字段名和隐藏字段
- **手动兜底**：识别不了时可在"高级设置"里逐项指定，适配移动 / 联通 / 电信
- **凭据加密**：账号密码用 Windows DPAPI（当前用户范围）加密保存，不明文落盘
- **开机自启**：写入 `HKCU\...\Run`，登录即接管，无需手动启动
- **防重复运行**：命名互斥量保证同时只有一个后台实例
- **`{local_ip}` 占位符**：地址里写 `{local_ip}` 会自动替换成当前网卡 IPv4

## 下载与运行

1. 到 [Releases](releases) 下载 `CampusPass-v<版本>-win-x64.zip`
2. 解压到**一个你不会再移动它的目录**（比如 `D:\CampusPass\`）
3. 双击 `CampusPass.exe`

单文件自包含，**不需要安装 .NET 运行时**，也不需要管理员权限。

> 首次启动会在 `%LOCALAPPDATA%\.NET\CampusPass` 解出约 8 MB 的 WPF 原生组件，之后复用。

## 使用方法

### 1. 填基本信息

打开后默认在「基础设置」页，填三项：

| 字段 | 说明 |
|---|---|
| 账号 / 密码 | 你的校园网账号密码（通常是学号） |
| 登录页地址 | 平时手动登录时浏览器跳出来的那个门户地址 |
| 表单提交地址 | 关闭自动识别时才需要手填 |

**登录页地址怎么找**：连上校园网 → 打开浏览器 → 随便访问一个 http 网站 →
地址栏跳过去的那个 URL 就是。形如
`http://10.254.1.2:8080/portal/login?...` 或 `http://x.x.x.x:8888/showLogin.do?...`。

### 2. 自动识别表单

保持「自动识别登录表单」开启，点 **识别表单**。程序会抓取该页面，解析出
提交地址、请求方式、账号/密码字段名和所有隐藏字段，并填到「高级设置」里。

看到"已识别登录表单"后，点 **保存并启用**。

### 3. 验证

点 **测试登录**，会返回这几种结果之一：

| 提示 | 含义 |
|---|---|
| 认证成功 | 一切正常 |
| 网络已经可以正常访问 | 当前已联网，没触发登录（也是正常的） |
| 已提交，但未确认联网 | 表单发了但没成功，见下方常见问题 |
| 认证失败：… | 地址不可达或网络异常 |

标题栏右侧的徽章会显示 **● 后台已启用**，表示开机启动项已写入。

### 4. 之后

不用再管它。开机登录后后台服务自动运行，断网重连、换网络、休眠唤醒都会自动重新认证。
要停掉：打开程序点 **停止后台**（会同时移除开机启动项）。

### 关于验证码和终端数限制

- **需要图形验证码的门户无法自动登录**——程序不会破解验证码。这类门户请在"高级设置"
  里关掉「自动识别登录表单」，确认它确实没有验证码字段后再试。
- **校园网限制同时在线设备数**（常见 2–3 台）时，超数量会登录失败，这与本程序无关。
- 部分门户用 JS 动态生成表单或走统一身份认证（CAS / OAuth），自动识别可能拿不到
  正确字段，需要手动配置。

## 界面说明

| 页面 | 内容 |
|---|---|
| 基础设置 | 账号密码、门户地址、自动识别开关、四个操作按钮 |
| 高级设置 | 提交方式、字段名、附加字段、联网检测地址、检查间隔、恢复默认 |
| 运行日志 | 后台服务的运行记录，可选尾部 500/800/2000 行 |
| 关于 | 版本、工作方式、数据与隐私说明、诊断信息 |

## 实测资源占用

在 2560×1440 @ 125% 的 Windows 11 上，对发布版单文件 exe 实测：

| 稳定态指标 | 旧版 1314.5.2.0（WinForms） | 当前版 |
|---|---|---|
| Private Bytes（已提交，不可换出） | 22.5 MB | **11.9 MB** |
| Working Set（当前常驻物理内存） | 29.5 MB | **约 4 MB** |
| 已连接空闲 90 秒的 CPU 增量 | 0 s | 0 s |
| 后台进程加载的程序集 | 61 | 47，**不含任何 WPF 程序集** |

后台服务与配置界面共用同一个 exe，但 `--background` 分支不引用任何 WPF 类型，
靠方法级惰性 JIT 把 `PresentationFramework`、`Wpf.Ui` 全部挡在进程外。
`tools/build.ps1` 里有一条静态检查：`Core/` 一旦出现 `using System.Windows` /
`Wpf.Ui` / `System.Drawing` 就**直接构建失败**，防止这个不变量被日后无意破坏。

Working Set 能低到 4 MB 是因为服务在确认联网、挂起轮询后会主动调用 `EmptyWorkingSet`
把干净的文件映射页交还系统；启动阶段峰值约 50 MB，静默后降下来。

## 数据与隐私

- 账号密码用 **DPAPI（当前用户范围）** 加密后保存在本机，同一台电脑的其他 Windows
  账户无法解密
- 程序**只**向你自己在"基础设置"里填写的门户地址提交登录表单
- **不包含**遥测、统计、广告代码，**不会**自行检查更新或上传任何信息
- 全部数据都在一个目录里：`%LocalAppData%\CampusPass\`
  （`settings.xml` / `credentials.dat` / `campuspass.log`）
- 界面固定为浅色主题

**彻底清除**：点"停止后台"（移除开机启动项）→ 删除 `%LocalAppData%\CampusPass\`
→ 删除程序目录。不留任何注册表项或服务。

## 从源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（缺失时构建脚本会给出 winget 安装命令）。

```powershell
git clone <本仓库地址>
cd CampusPass
powershell -ExecutionPolicy Bypass -File .\tools\build.ps1
```

产物写入 `outputs\CampusPass.exe` 并打包为 `outputs\CampusPass-v<版本>-win-x64.zip`。

| 开关 | 作用 |
|---|---|
| `-Tests` | 构建前先跑 Core 层回归检查 |
| `-Compress` | 压缩内嵌程序集，exe 约 120 MB，但启动时需解压进内存 |
| `-FrameworkDependent` | 产出 2–5 MB 的小 exe，要求目标机已装 .NET 8 Desktop Runtime |
| `-SkipPublish` | 只做编译和边界检查 |
| `-FreshIcon` | 从 `work/assets/CampusPass.svg` 重新生成图标（需 `pip install pycairo`） |

### 项目结构

```
app/src/CampusPass/
  Core/      认证、配置、凭据、后台服务、开机启动 —— 只依赖 BCL，禁止引用 WPF
  Gui/       WPF + WPF-UI 界面 —— 只有这里可以引用 WPF
  Program.cs 入口：--background 走后台，否则走界面
tools/       构建、图标生成、后台行为与内存验证、窗口截图
tests/       Core 层回归检查
work/        改名前的 WinForms 旧实现，保留作对照和回退
outputs/     构建产物（已在 .gitignore 中，通过 Releases 分发）
```

### 辅助脚本

| 脚本 | 用途 |
|---|---|
| `tools/build.ps1` | 构建 + 发布 + 打包 |
| `tools/verify-background.ps1` | 验证后台服务：单实例、优雅停止、内存、是否误加载 WPF |
| `tools/measure-memory.ps1` | 采样后台服务的 Private Bytes / Working Set / CPU |
| `tools/stop-service.ps1` | 通过命名事件优雅停止后台服务（重建前用） |
| `tools/inspect-state.ps1` | 查看启动项、进程、数据目录、互斥量状态 |
| `tools/make_icon.py` | 从 SVG 生成多尺寸 .ico 和界面用 PNG |

## 改名说明（CampusFlow → CampusPass）

程序原名 CampusFlow。改名涉及几处会**静默失效**的地方，已专门处理：

- **开机启动项**：注册表值的名称和路径都变了。旧值指向的路径不存在时，下次登录会无声
  失败。打开一次配置界面即会自动接管：检测到注册项指向别的二进制时改写并重启服务。
- **命名互斥量与停止事件**：改为 `Local\CampusPass.*`。因为旧实例认不到新事件名，
  `Stop()` 会同时通知旧名，避免新旧两个后台实例并存、同时向门户提交登录。
- **数据目录**：`%LocalAppData%\CampusFlow\` → `CampusPass\`。首次启动自动搬运配置、
  凭据和日志（DPAPI 同机同账户可直接解密，**不需要重新输入密码**），旧目录保留作回退。
- 程序集名、exe 名、命名空间、窗口标题、User-Agent。

命令行参数 `--background` 保持不变。`settings.xml` 的元素顺序和命名空间未变，
新旧版本可互相读写。

## 第三方组件

| 组件 | 用途 | 许可证 |
|---|---|---|
| [.NET 8](https://github.com/dotnet/runtime) | 运行时（自包含内置） | MIT |
| [WPF-UI](https://github.com/lepoco/wpfui) by lepo.co | Fluent 控件、Mica 背景、主题 | MIT |
| [Fluent System Icons](https://github.com/microsoft/fluentui-system-icons) | 界面图标（随 WPF-UI 引入） | MIT |

WPF-UI 自身还传递引入了 VirtualizingWrapPanel、dotnet/wpf、WinUI 和 Segoe Fluent
Icons 等组件，完整清单见其包内 `ThirdPartyNotices.txt`。

## 许可证

本项目以 [MIT License](LICENSE) 授权。
