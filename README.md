# Codex 限额悬浮窗

一个本地、无遥测的 Windows 小组件，用来显示 Codex 每周剩余额度、重置时间和“完全重置”到期时间。

## 功能

- 桌面悬浮窗，可拖动、置顶、调节不透明度
- 系统托盘常驻，图标直接显示每周剩余百分比
- 可选任务栏百分比图标
- 当前用户开机启动
- 每个重置周期只提醒一次的低额度通知（默认 20%）
- 自动刷新和双击手动刷新
- 自动读取完全重置到期信息；接口未提供时可手动录入
- 不读取、复制或保存 `auth.json`、浏览器 Cookie、密码或 API Key

## 使用

运行 `CodexQuotaWidget.exe`。右键托盘图标可调整设置；双击托盘图标恢复悬浮窗。

程序通过 Codex 本机 `app-server` 的只读 `account/rateLimits/read` 方法获取额度。需要已安装并使用 ChatGPT 账户登录的 Codex CLI/桌面客户端。如果商店版 Codex 不允许外部启动其组件，可另外安装官方 Codex CLI，程序会自动发现它。

点击窗口的 `—` 或 `×` 会隐藏到系统托盘。双击托盘图标或再次运行 EXE 都会恢复已有窗口；需要完全关闭时，请右键托盘图标并选择“退出”。

配置保存在 `%LOCALAPPDATA%\CodexQuotaWidget\settings.json`。卸载时先从托盘退出并关闭“开机启动”，然后删除程序与该配置目录即可。

## 本地构建

```powershell
dotnet publish .\CodexQuotaWidget\CodexQuotaWidget.csproj -c Release -r win-x64 --self-contained true
```

使用 `--demo` 参数可以在无 Codex 数据源时查看界面。

## 隐私与发布

- 不包含用户配置、额度数据、账户 ID、用户名或本机绝对路径。
- 不读取 `auth.json`，不收集浏览器 Cookie，不内置遥测或更新服务。
- 开机启动只写入当前用户的 Windows `Run` 注册表项。
- 项目采用 MIT 许可证。它是非官方社区工具，与 OpenAI 无隶属或背书关系。
