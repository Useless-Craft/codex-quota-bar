# Codex Quota Bar

[English](README.en.md) · [下载 Windows 版本](https://github.com/Useless-Craft/codex-quota-bar/releases/latest) · [反馈问题](https://github.com/Useless-Craft/codex-quota-bar/issues)

在 Windows Codex 桌面窗口顶部、**Help / 帮助** 右侧显示每周剩余额度和下次重置时间。

**每周额度剩余 11%**　│　重置时间 2026年9月6日 19:29

**Weekly usage limit 11% left**　│　Resets Sep 6, 2026, 19:29

右侧第三段统一显示为 `N% reset`：有效重置预告显示红色 `100% reset`；没有有效预告时显示 24 小时实验性概率，例如 `20% reset`。预告结束或来源撤销后恢复概率，已发生的重置动态不再占据顶部。`100%` 表示来源存在有效预告，不代表额度已经到账，也不是 OpenAI 对重置结果的保证。

以上是格式示例。实际数值来自当前登录的 Codex 账户。

## 功能

- 圆角胶囊显示周剩余额度和重置时间，并在空间足够时增加 Tibo 状态；空间不足时保留原有两项，完整状态仍可悬停查看。
- 自动适配亮色 / 暗色主题；剩余不超过 20% 时显示橙色，不超过 10% 时显示红色。
- 跟随 Codex 界面语言：中文使用中文，其余语言使用英文；时间按本机时区、24 小时制显示。
- 支持多个 Codex 窗口，监听窗口移动事件以即时跟随；随窗口最小化、遮挡和关闭。
- 每 60 秒共享刷新一次额度；右键可手动刷新或退出。
- 每 15 分钟异步刷新公开重置动态，两个只读请求并行执行，最长等待 30 秒；不发送账户 ID、额度、登录信息或 X 凭据。
- 有效预告显示红色 `100% reset`，否则显示来源给出的 24 小时概率；已发生的重置或 banked reset 不会把概率遮住三天。无可用数据时显示 `--% reset`。
- 悬停提示包含原帖、数据来源、更新时间、有效期和实验性说明；右键可打开数据来源页面。
- 专用快捷方式同时启动 Codex 和额度条；最后一个 Codex 窗口关闭后，工具及其读取进程退出。

## 运行要求

- Windows 10 / 11，x64，.NET Framework 4.8。
- 已安装并登录的 Codex Windows 桌面应用，顶部菜单栏可见。首次使用前先正常打开一次 Codex。
- 不支持浏览器版、macOS 或 Linux。

这是社区独立工具，与 OpenAI 无隶属关系。它不修改 Codex 安装文件；菜单结构、CLI 位置或接口随 Codex 更新变化时，仍可能需要适配。发布程序未进行代码签名。

## 下载与使用

1. 从 [Releases](https://github.com/Useless-Craft/codex-quota-bar/releases/latest) 下载 `codex-quota-bar-v1.1.7-windows-x64.zip`。
2. **完整解压**到一个准备长期保留的目录。
3. Codex 已打开时，双击 `CodexQuotaBar.exe` 即可显示额度。

要让 Codex 和额度条一起启动，在解压目录打开 PowerShell，运行一次：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\create-shortcut.ps1
```

随后使用开始菜单中的 **Codex + Quota Bar** 快捷方式；也可以手动将该快捷方式固定到任务栏。脚本只为当前用户创建一个快捷方式，不需要管理员权限。上述执行策略只作用于这一次 PowerShell 进程。

创建到桌面或其他目录：

```powershell
.\create-shortcut.ps1 -ShortcutDirectory ([Environment]::GetFolderPath('Desktop'))
```

快捷方式指向解压目录，移动工具后请重新创建。原有 Codex 入口仍可使用，需要联动时使用新快捷方式。本工具不设置开机启动，不安装常驻监视器。

同一用户会话只运行一个实例，再次启动不会叠加额度条。右键任意额度条选择“退出额度显示”，会退出所有额度条，Codex 保持运行。

## 从源码构建

无需 Visual Studio、NuGet 或第三方库，使用本机 .NET Framework 编译器：

```powershell
git clone https://github.com/Useless-Craft/codex-quota-bar.git
cd codex-quota-bar
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

生成的 `CodexQuotaBar.exe` 位于仓库根目录。重新编译或覆盖更新前，请先退出该目录中运行的额度工具。

| 文件 | 用途 |
| --- | --- |
| `QuotaBar.cs` | WPF 显示、窗口跟随、额度读取与进程生命周期 |
| `quota.manifest` | 普通用户权限与 DPI 声明 |
| `build.ps1` | x64 编译 |
| `create-shortcut.ps1` | 创建联动启动快捷方式 |
| `tests/test-forecast.ps1` | 重置动态解析测试 |

## 工作方式与数据

工具通过已安装的 Codex CLI 启动自有 `app-server --stdio` 子进程，调用 `account/rateLimits/read`。仅显示 `codex` 额度桶中长度为 10080 分钟的周额度；缺失数据不会当作 0%，到达重置时间后等待服务返回新数据。

第三段读取 [codex-reset.com](https://codex-reset.com/) 的公开只读 JSON：`/api/forecast` 提供 24 小时实验性概率和当前有效预告，`/api/feed` 提供 Tibo 原帖及已公布的 usage reset / banked reset，供悬停提示查看。仅有效预告覆盖概率，预告结束后恢复概率。接口可能延迟、不可用或改变，且不代表 OpenAI 的服务承诺。右键菜单中的 **打开重置信息（codex-reset.com）** 会打开数据来源页面。

工具复用 Codex 现有登录，不发起模型对话、购买额度或使用重置券，也没有额外的遥测或上传服务。语言只读 `CODEX_HOME/computer-use/config.json` 中的 `locale`，未设置 `CODEX_HOME` 时使用当前用户的 `.codex` 目录。暂时无法读取时保留上次语言，初始默认为英文。

菜单位置由临时 UI Automation 子进程读取，5 秒超时后终止，保留上次有效位置。正常每 30 秒复核，失败后每 10 秒重试；新窗口、语言或 DPI 变化会触发重新定位。窗口移动使用缓存位置与 WinEvent，不等待菜单读取完成。重置动态请求在独立异步流程中执行，不阻塞移动、菜单定位、额度读取或退出；短暂失败时保留仍在有效期内的上次结果。

## 常见问题与排查

- **看不到额度条**：确认顶部 Help / 帮助可见、窗口足够宽，稍等菜单定位；最小化或空间不足时会隐藏。读取额度失败显示“未更新”，可右键刷新。
- **更新后启动较慢**：`--launch` 最多等待 120 秒让 Codex 窗口出现；超过时限后可等 Codex 打开，再运行工具。
- **其他错误**：未处理错误会在同目录覆盖写入 `last-error.txt`，包含时间、发生阶段和异常堆栈。正常运行不持续写日志。

命令行操作：

```powershell
.\CodexQuotaBar.exe --launch
.\CodexQuotaBar.exe --exit
.\CodexQuotaBar.exe --check "$PWD\quota-check.json"
```

`--check` 只读检查实际额度、菜单定位及日期格式。报告包含账户 ID 和窗口标题；提交 issue 前请删除这些字段及日志中的个人路径。日志和检查报告不应提交进 Git。

卸载时先退出额度条，再删除工具目录和自己创建的快捷方式即可。

## 许可证

[MIT](LICENSE)。本仓库不包含 Codex 应用文件或 OpenAI 图标。
