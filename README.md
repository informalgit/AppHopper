[English](README.en.md) | 简体中文

# AppHopper

**Windows 上的应用级 `Alt+Tab`，外观 UI 来自 PowerToys Window Hopper**

两个快捷键, 职责完全分开:

| 快捷键 | 在什么之间切换 | 行为 |
|---|---|---|
| `Alt+Tab` | **应用** | 每个应用只占一个条目，按最近使用排序；前台应用的兄弟窗口不出现，所以 `Alt+Tab` 永远落在*另一个*应用上 |
| ``Alt+` `` | **前台应用的窗口** | 交给 [PowerToys Window Hopper](https://learn.microsoft.com/zh-cn/windows/powertoys/window-hopper)(或任何应用内切换器)处理 |

再也不用在五个资源管理器窗口之间"路过"才能到达浏览器：`Alt+Tab` 直接跳到下一个*应用*，``Alt+` `` 在当前应用内部循环

## 构建

1. **最简单的做法：双击仓库里的 `build.bat`**，它会先结束正在运行的实例，并嵌入 `app.manifest`，使生成的 exe 启动时自动申请管理员权限；若已有实例正以管理员权限运行，**请先从托盘退出**，否则输出文件被占用会导致编译失败。
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
