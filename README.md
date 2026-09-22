[English](README.en.md) | 简体中文

# AppHopper

**Windows 上的应用级 `Alt+Tab`，外观 UI 来自 PowerToys Window Hopper**

两个快捷键, 职责完全分开:

| 快捷键 | 在什么之间切换 | 行为 |
|---|---|---|
| `Alt+Tab` | **应用** | 每个应用只占一个条目，按最近使用排序；前台应用的兄弟窗口不出现，所以 `Alt+Tab` 永远落在*另一个*应用上 |
| ``Alt+` `` | **前台应用的窗口** | 交给 [PowerToys Window Hopper](https://learn.microsoft.com/zh-cn/windows/powertoys/window-hopper)(或任何应用内切换器)处理 |

再也不用在五个资源管理器窗口之间"路过"才能到达浏览器：`Alt+Tab` 直接跳到下一个*应用*，``Alt+` `` 在当前应用内部循环

## 特性

- **真正的应用级 `Alt+Tab`** —— 窗口按进程分组,按 Z 序(最近使用)排序。每个组的代表窗口是其最近使用的窗口,因此切回某个应用时,你会回到上次离开的地方。
- **Window Hopper 风格 UI** —— 浮层完整移植自 PowerToys 的 `AltWindowCycle` 模块:WinUI 风格圆角卡片、每卡片图标+标题头、系统强调色双环选中框、跟随系统的深浅主题、带页码指示的分页,以及**不做背景变暗**。
- **DWM 实时缩略图** —— 与任务栏预览同机制的实时合成画面,按卡片比例居中裁剪,绝不拉伸变形。
- **完整的鼠标支持** —— 点击卡片直接切换、点击面板外任意位置取消、滚轮循环选择。(`Esc` 同样可以取消。)
- **虚拟桌面感知** —— 只列出*当前*桌面上的窗口(通过公开的 `IVirtualDesktopManager` 判断),切换永远不会把窗口拽到别的桌面。
- **低侵入** —— 切换器是纯浮层 UI，不修改其他窗口的样式、可见性、归属或任务栏属性；如果当前无法建立至少两个候选应用，只重放这次 `Tab` 交还给 Windows 原生切换器，不留下需要清理的状态。
- **按显示器、DPI 感知** —— 面板显示在前台窗口所在显示器的中央,按该显示器 DPI 缩放,最多 6 列并自动分页。
- **单文件、零依赖** —— 一个 C# 源文件,用 Windows 自带的编译器即可构建。无需安装器、无需装运行库,绿色单 exe。


## 构建

1. **最简单的做法：双击仓库里的 `build.bat`**，它会先结束正在运行的实例，并嵌入 `app.manifest`，使生成的 exe 启动时自动申请管理员权限；若已有实例正以管理员权限运行，**请先从托盘退出**，否则输出文件被占用会导致编译失败。
2. 手动执行等价命令:
    ```bat
    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe -platform:anycpu -optimize+ -win32manifest:app.manifest ^
      -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll ^
      -out:AppHopper.exe AppHopper.cs
    ```
适用于 Windows 10 和 11。

## 使用

运行 `AppHopper.exe`——托盘出现图标(右键:*Enabled*、*Start with Windows*、*Exit*)。然后:

| 输入 | 动作 |
|---|---|
| `Alt+Tab` | 打开切换条,预选下一个应用 |
| 按住 `Alt`,连按 `Tab` / `Shift+Tab` | 向后 / 向前循环 |
| 松开 `Alt` | 切换到选中的应用(最小化的会还原) |
| `Esc` / 点击面板外 | 取消 |
| 点击某张卡片 | 立即切换到该应用 |
| 鼠标滚轮 | 循环选择 |

应用列表每次打开切换器时实时计算，没有任何需要配置的选项。诊断：以 `--log` 参数启动；日志最多 8 MiB，默认隐藏窗口标题和程序名，需要完整枚举细节时使用 `--log-verbose`。若仍看到 Windows 原生切换条，日志中的 `hotkey: alt+tab -> start`、`start aborted:` 和 `alt+tab fallback:` 可以区分钩子未到达、候选不足和程序主动回退。运行 `AppHopper.exe --self-test`（或双击 `self-test.bat`）可执行纯逻辑回归自检，退出码 0 表示通过。`Start with Windows` 仅允许安装在 `Program Files` 下的受保护路径；由于程序要求管理员权限，登录时可能触发 UAC。

## 实现原理(简版)

- 低级键盘钩子在切换器会话期间吞掉 `Alt+Tab`/`Esc`；若启动条件不满足，程序会把这次 `Tab` 交给 Windows 原生切换器，其余按键原样放行。
- 顶层窗口按 Z 序枚举,经经典 Alt-Tab 资格规则 + 当前桌面检查过滤,再按进程映像路径分组(UWP 窗口通过其子 `Windows.UI.Core.CoreWindow` 归因到真实应用)。
- 实时预览是 `DwmRegisterThumbnail` 合成画面,渲染进一块不透明圆角面板;卡片层(标题、描边、选中环、页码)用 GDI+ 画进预乘 alpha 的 DIB,再经 `UpdateLayeredWindow` 合成——与其移植来源的 PowerToys 模块相同的双层设计。
- 激活目标窗口使用经典的 `AttachThreadInput` 前台切换;最小化窗口先还原再聚焦。

## 致谢

- [PowerToys Window Hopper(`AltWindowCycle`)](https://github.com/microsoft/PowerToys) —— 浮层 UI 与布局移植自该模块(MIT)。欢迎给 PowerToys 点星,顺便看看其余的好东西。
- [alt-tab-macos](https://github.com/lwouis/alt-tab-macos) —— 应用级切换 + 预览的最初灵感来源。
- [window-switcher](https://github.com/sigoden/window-switcher) —— ``Alt+` `` 式应用内切换的先行者。

## 许可证

[MIT](LICENSE) © 2026 informalgit。源自 PowerToys 的 UI 代码仍受 MIT 许可约束，完整第三方声明见 [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt)。
