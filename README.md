[English](README.en.md) | 简体中文

# AppHopper

**Windows 上的应用级 `Alt+Tab`，外观 UI 来自 PowerToys Window Hopper**

两个快捷键, 职责完全分开:

| 快捷键 | 在什么之间切换 | 行为 |
|---|---|---|
| `Alt+Tab` | **应用** | 每个应用只占一个条目，按最近使用排序；前台应用的兄弟窗口不出现，所以 `Alt+Tab` 永远落在*另一个*应用上 |
| ``Alt+` `` | **前台应用的窗口** | 交给 [PowerToys Window Hopper](https://learn.microsoft.com/zh-cn/windows/powertoys/window-hopper)(或任何应用内切换器)处理 |

再也不用在五个资源管理器窗口之间"路过"才能到达浏览器：`Alt+Tab` 直接跳到下一个*应用*，``Alt+` `` 在当前应用内部循环

## 1.3

### bug fix
- 无独立修复。

### features
- 托盘新增 `Get updates...`：确认后从 GitHub 下载正式版，校验后原位替换并重新启动；替换或启动失败时尝试恢复旧版。

## 构建

1. **最简单的做法：双击仓库里的 `build.bat`**，它会先结束正在运行的实例，并嵌入 `app.manifest`，使生成的 exe 启动时自动申请管理员权限；若已有实例正以管理员权限运行，**请先从托盘退出**，否则输出文件被占用会导致编译失败。
2. 手动执行等价命令:
    ```bat
    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe -platform:anycpu -optimize+ -win32manifest:app.manifest ^
      -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll ^
      -out:AppHopper.exe AppHopper.cs
    ```
适用于 Windows 10 和 11。

## 获取更新

右键托盘 → `Get updates...`。仅在点击时检查 GitHub 最新正式版；发现更高版本后询问是否下载、替换并重启。检查、确认和下载期间不允许重复发起更新。下载过程中切换器仍可使用，不运行常驻更新服务，也不需要单独服务器。

下载必须匹配 GitHub 附件的大小、SHA-256 和 exe 版本；缺少校验信息、网络错误或下载不完整时不退出旧程序。更新助手临时存放在仅管理员/SYSTEM 可修改的 `ProgramData` 目录；安装在其他盘符时，先在安装目录内创建并锁住同卷受保护暂存目录。确认准备成功后，让旧进程退出、原子替换原路径并启动新版；新版未在 15 秒内完成启动则尝试恢复并启动旧版。恢复也失败时弹窗保留备份位置，供手动恢复。

原路径和自启动注册项不变，保留 Enabled 状态及日志模式。不支持网络路径或经过重解析点的安装目录；文件被占用或无法安全替换时报告失败。运行中的临时助手无法立即删除，安排下次 Windows 重启时清理。1.2 没有此菜单，需要先手动安装带更新功能的版本，之后才能原位更新。

## 致谢

- [PowerToys Window Hopper(`AltWindowCycle`)](https://github.com/microsoft/PowerToys) —— 浮层 UI 与布局移植自该模块(MIT)。欢迎给 PowerToys 点星,顺便看看其余的好东西。
- [alt-tab-macos](https://github.com/lwouis/alt-tab-macos) —— 应用级切换 + 预览的最初灵感来源。
- [window-switcher](https://github.com/sigoden/window-switcher) —— ``Alt+` `` 式应用内切换的先行者。

## 许可证

[MIT](LICENSE) © 2026 informalgit。源自 PowerToys 的 UI 代码仍受 MIT 许可约束，完整第三方声明见 [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt)。
