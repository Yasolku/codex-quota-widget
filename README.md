# Codex 5 小时与周额度悬浮窗

一个本地、无遥测的 Windows 小组件，用于同时显示 Codex 5 小时额度、每周额度、各自重置时间和“完全重置”到期时间。

主界面将更紧迫的 **5 小时额度放大显示**，每周额度作为较小的固定信息行显示，不使用容易混淆的交替轮播。托盘图标、任务栏图标和低额度提醒均以 5 小时额度为准。

## 功能

- 桌面悬浮窗，可拖动、置顶、切换紧凑模式和不透明度
- 5 小时剩余额度、进度条和准确重置时间作为主显示
- 每周剩余额度及重置时间始终作为次要信息显示
- 系统托盘常驻，图标直接显示 5 小时剩余百分比
- 可选任务栏 5 小时百分比图标
- 当前用户开机启动
- 5 小时额度低于 10%、20% 或 30% 时通知，每个 5 小时周期只提醒一次
- 15 秒、1 分钟或 5 分钟自动刷新，支持手动刷新
- 显示“完全重置”次数及到期时间；接口未提供时可手动录入
- 隐藏后再次运行 EXE，可唤醒现有窗口而不会重复启动
- 不读取、复制或保存 `auth.json`、浏览器 Cookie、密码或 API Key

## 环境要求

- Windows 10 或 Windows 11，64 位（x64）
- [Microsoft .NET 9 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/9.0/runtime)
- 已安装并登录 ChatGPT 账户的 Codex Desktop 或 Codex CLI
- Codex 安装需要支持本机 `app-server` 接口
- 读取最新账户限额时需要网络连接

请安装下载页面中的 **.NET Desktop Runtime**，不要只安装普通的 `.NET Runtime`。如果运行 EXE 时提示缺少 `Microsoft.WindowsDesktop.App 9.0`，说明 Desktop Runtime 尚未安装或架构不匹配。

## 安装

1. 构建或下载最新的 `CodexQuotaWidget-v3.x.x-framework-dependent-win-x64.zip`。
2. 对照同一 Release 中的 `SHA256SUMS.txt` 校验下载文件。
3. 解压到固定目录，不要直接在 ZIP 内运行。
4. 双击 `CodexQuotaWidget.exe`。
5. 如需开机运行，右键托盘百分比图标并启用“开机启动”。

程序当前没有商业代码签名，因此 Windows SmartScreen 可能显示“未知发布者”。请只从本仓库的 Release 页面下载并核对 SHA-256。

## 使用

- 拖动悬浮窗可改变位置。
- 双击悬浮窗或选择托盘菜单“立即刷新”可手动刷新。
- 点击窗口的 `—` 或 `×` 会隐藏到系统托盘。
- 双击托盘图标或再次运行 EXE 会恢复已有窗口。
- 需要完全关闭时，请右键托盘图标并选择“退出”。

程序通过 Codex 本机 `app-server` 的只读 `account/rateLimits/read` 方法获取额度，并分别识别 300 分钟与 10080 分钟窗口。配置保存在 `%LOCALAPPDATA%\CodexQuotaWidget5hWeekly\settings.json`，单实例标识和开机启动项也与旧版分离，因此两个版本可以同时运行。

## 显示规则

- 大号百分比、进度条：5 小时额度
- 小号固定信息行：每周额度
- 托盘和任务栏图标：5 小时额度
- 低额度提醒：5 小时额度
- 某个周期未由接口返回时，只将该周期显示为“不可用”，不会用另一个周期的数据代替

## 隐私与安全

- 没有遥测、广告或自动更新服务。
- 不读取 `auth.json`，不收集浏览器 Cookie，不内置账户令牌。
- 额度请求由已安装的 Codex 进程完成，应用只读取返回的限额数据。
- 不包含用户配置、额度数据、账户 ID、用户名或构建电脑绝对路径。
- 开机启动只写入当前用户的 Windows `Run` 注册表项。

## 本地构建

需要 .NET 9 SDK：

```powershell
.\build.ps1 -Runtime win-x64
```

或者：

```powershell
dotnet publish .\CodexQuotaWidget\CodexQuotaWidget.csproj -c Release -r win-x64 --self-contained false
```

输出位于 `artifacts\win-x64`。使用 `--demo` 参数可以在无 Codex 数据源时查看界面。

## 许可证与声明

项目采用 MIT 许可证。它是非官方社区工具，与 OpenAI 无隶属关系，也未获得 OpenAI 背书。
