[English](README.en.md) | 简体中文

# AppHopper

<p align="center">
  <img src="assets/apphopper-demo-zh.gif" alt="AppHopper demo" width="960">
</p>

**Windows 上的跨应用 `Alt+Tab` 窗口切换器，外观 UI 来自 PowerToys Window Hopper**

两个快捷键, 职责完全分开:

| 快捷键 | 在什么之间切换 | 行为 |
|---|---|---|
| `Alt+Tab` | **其他应用的窗口** | 当前应用只保留前台窗口，其兄弟窗口不出现；其他应用的每个可切换窗口各占一个条目，按最近使用顺序排列 |
| ``Alt+` `` | **前台应用的窗口** | 交给 [PowerToys Window Hopper](https://learn.microsoft.com/zh-cn/windows/powertoys/window-hopper)(或任何应用内切换器)处理 |

从其他应用切到资源管理器时，每个可切换的资源管理器窗口都会分别出现，不再只显示一个代表；当前应用内部的兄弟窗口仍不出现，``Alt+` `` 继续负责当前应用内部循环。

## 构建

1. **最简单的做法：双击仓库里的 `build.bat`**：它先编译出候选 exe，成功后才结束正在运行的实例并替换目标，所以编译失败不会影响正在运行的程序和现有的 exe。嵌入的 `app.manifest` 让生成的 exe 启动时自动申请管理员权限。由计划任务启动的提权实例也能被停掉；手动启动、又没有对应计划任务的提权实例，请先从托盘退出。
2. 手动执行等价命令:
    ```bat
    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe -platform:anycpu -optimize+ -win32manifest:app.manifest ^
      -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll ^
      -out:AppHopper.exe AppHopper.cs
    ```
适用于 Windows 10 和 11。


## 致谢

- [PowerToys Window Hopper(`AltWindowCycle`)](https://github.com/microsoft/PowerToys) —— 浮层 UI 与布局移植自该模块(MIT)。欢迎给 PowerToys 点星,顺便看看其余的好东西。
- [alt-tab-macos](https://github.com/lwouis/alt-tab-macos) —— 应用级切换 + 预览的最初灵感来源。
- [window-switcher](https://github.com/sigoden/window-switcher) —— ``Alt+` `` 式应用内切换的先行者。

## 许可证

[MIT](LICENSE) © 2026 informalgit。源自 PowerToys 的 UI 代码仍受 MIT 许可约束，完整第三方声明见 [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt)。
