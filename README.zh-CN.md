# Pix

[English](./README.md) | [日本語](./README.ja.md)

Pix 是 [pi 编程智能体](https://github.com/badlogic/pi-mono) 的 Windows 桌面伴侣，包含两个部分：

- **pi-web** —— pi 的本地网页界面（fork 自 [agegr/pi-web](https://github.com/agegr/pi-web)）：会话管理、实时对话、模型配置、技能管理和项目文件预览。
- **Pix Launcher（启动器）** —— 原生 Windows 控制平面：以悬浮球形态常驻桌面，一键启动 pi-web 服务，并在弹层面板里展示已配置模型的余额与订阅额度。

![浏览器里的 Pi Web](https://raw.githubusercontent.com/Acidmoon/Pix/dev/docs/screenshot2.png)

## Pix 启动器

启动器是 Pix 区别于原版 pi-web 的部分——一个安静的 WinForms 小部件：

- **悬浮球**：可拖动、永远置顶的深色圆球。点击后深色卫星面板在旁边淡入滑出，球体本身全程零重绘、不移动。
- **一键服务控制**：在随机回环端口启动/停止 pi-web，等待健康检查就绪后，自动打开独立的 Edge/Chrome `--app` 窗口。
- **浏览器吸附**：应用窗口打开后，悬浮球会滑到窗口右边缘磁吸跟随；手动拖球即脱离，位置自动记忆。
- **余额与额度面板**：一眼看清账户余量——
  - DeepSeek：分币种显示总额 / 充值 / 赠送余额
  - MiniMax 编程套餐（国内与国际站）：5 小时窗口与 7 天窗口的剩余百分比和重置时间
  - Kimi：Code 5 小时 / 7 天额度剩余百分比，详情行附订阅与赠送额度
- **服务状态**：PID、端口、活跃 AgentSession 数；系统托盘菜单镜像全部控制项。
- **细节体验**：150–240ms 的克制动画、隐藏式滚动条、固定的专用浏览器 profile（扩展和登录态持久保留，不再每次启动重复弹首次运行窗口）、与悬浮球统一的应用图标。

## 语音输入

Pix 内置端侧语音转文字（[SenseVoice](https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17)，经 sherpa-onnx 本地运行——不上云、不需要 API key），中 / 英 / 日 / 韩 / 粤自动识别语种。

- **网页界面内**：点聊天输入框的麦克风按钮开始说话，再点一次停止，识别文本插入光标处。
- **系统级全局**：在启动器面板（或托盘菜单）打开"语音输入"，然后在任意应用里按住 **右 Ctrl** 说话、松开，文字自动输入光标处；右 Alt 是第二绑定键。长文本自动改用剪贴板粘贴。

### 模型下载（一次性）

模型约 240MB，不随仓库分发（已 gitignore）。下载 SenseVoice int8 版本，把下面两个文件放进应用旁的 `models/sensevoice-small-int8/` 目录：

- `model.int8.onnx`
- `tokens.txt`

下载地址：[Hugging Face](https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17) · [ModelScope 镜像](https://www.modelscope.cn/models/danieldong/sensevoice-small-onnx-quant)。想放在别的位置，用环境变量 `PIX_VOICE_MODEL_DIR` 指向模型目录即可。模型缺失时麦克风按钮会变琥珀色，点击它会看到这段说明。

## 运行环境

- Windows 10/11
- `PATH` 上可用的 [Node.js](https://nodejs.org/) 22.19.0 或更高版本
- [.NET 8 桌面运行时](https://dotnet.microsoft.com/download)（仅启动器需要；从源码构建则需要 .NET 8 SDK）

## 开始使用

### 方式 A：只用 pi-web

pi-web 可以脱离启动器在全平台独立运行：

```bash
npx @agegr/pi-web@latest
# 或
npm install -g @agegr/pi-web
pi-web
```

启动后打开 [http://127.0.0.1:30141](http://127.0.0.1:30141)。命令行版本会在服务就绪后尝试自动打开浏览器。Pi Web 默认仅监听 `127.0.0.1`。

**可选参数：**

```bash
pi-web --port 8080              # 自定义端口
pi-web --hostname 0.0.0.0       # 在可信网络中开放访问
pi-web -p 8080 -H 0.0.0.0       # 组合使用
pi-web --no-open                # 不自动打开浏览器

PORT=8080 pi-web                # 也支持环境变量
PI_WEB_HOSTNAME=0.0.0.0 pi-web  # 显式开放网络访问
PI_WEB_ALLOWED_HOSTS=pi-web.internal pi-web  # 允许指定的代理或自定义主机名
PI_WEB_NO_OPEN=1 pi-web         # 适用于后台服务或开机自启
```

Pi Web 没有应用层身份验证，并且可以调用高权限智能体。请勿将其暴露到互联网；仅在可信网络中使用非 loopback 监听地址。
API 请求仅接受 loopback 名称、IP 字面量、当前监听主机名，以及 `PI_WEB_ALLOWED_HOSTS` 中以逗号分隔的精确主机名。可信反向代理使用不同的外部主机名时，请配置该变量。

#### HTTP 代理

Pi Web 的服务端模型请求和 API 请求会读取标准的 `HTTP_PROXY`、`HTTPS_PROXY` 和 `NO_PROXY` 环境变量。

macOS 或 Linux：

```bash
HTTP_PROXY=http://127.0.0.1:7890 \
HTTPS_PROXY=http://127.0.0.1:7890 \
NO_PROXY=localhost,127.0.0.1 \
npx @agegr/pi-web@latest
```

Windows PowerShell：

```powershell
$env:HTTP_PROXY = "http://127.0.0.1:7890"
$env:HTTPS_PROXY = "http://127.0.0.1:7890"
$env:NO_PROXY = "localhost,127.0.0.1"
npx @agegr/pi-web@latest
```

### 方式 B：从源码构建 Pix（pi-web + 启动器）

```powershell
# 1. 安装依赖并构建 pi-web（生产构建）
npm install
npm run build

# 2. 构建启动器
dotnet build .\launcher\PixLauncher.csproj -c Release

# 3. 运行——它会从自身位置向上查找 bin\pi-web.js
.\launcher\bin\Release\net8.0-windows\PixLauncher.exe
```

如果启动器放在仓库之外的目录，用环境变量指向 pi-web 目录：

```powershell
$env:PI_WEB_ROOT = "E:\Pix"
```

[GitHub Releases](https://github.com/Acidmoon/Pix/releases) 附有打包好的启动器压缩包——里面只有启动器本身，仍需要一份 `npm run build` 出来的 pi-web 供它驱动。

## 架构关系

```
PixLauncher.exe（WinForms，控制平面）
   │  仅回环 HTTP
   │   GET  /api/health              （就绪探活，无鉴权）
   │   GET  /api/launcher/status     （余额与额度，Bearer token）
   │   POST /api/launcher/shutdown   （优雅关停，Bearer token）
   ▼
node bin/pi-web.js（Next.js 服务）
   ▼
Edge/Chrome --app 窗口（复用用户现有 Default profile）
```

- 两个进程不共享任何代码，启动器只通过 HTTP 与 pi-web 通信。
- 每次启动生成随机的 `PI_WEB_LAUNCHER_TOKEN`；没有它，`/api/launcher/*` 一律 404——普通方式运行 pi-web（CLI、npx、dev server）不暴露任何特权接口。
- 模型凭据通过 pi 自己的认证存储解析，只用于服务端向对应官方接口发起的请求，永远不会返回给启动器或浏览器。

## 开发

```bash
npm install
npm run dev          # pi-web 运行在 http://localhost:30141

dotnet build .\launcher\PixLauncher.csproj
dotnet run --project .\launcher\PixLauncher.csproj
```

常用检查：

```bash
node_modules/.bin/tsc --noEmit
npm run lint
```

dev server 运行期间不要执行 `next build`，会污染 `.next/`。Windows 上做生产构建前请先隔离构建环境（见 [docs/launcher.md](./docs/launcher.md)）。

## 文档

- [启动器详解](./docs/launcher.md)——功能范围、安全模型、provider 适配器
- [pi-web 里的 Worktree](./docs/worktrees.zh-CN.md)——侧边栏的分支切换
- [AGENTS.md](./AGENTS.md)——架构说明与开发约定

## 许可证

MIT，与上游 [pi-web](https://github.com/agegr/pi-web) 一致。
