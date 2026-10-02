[English](README.en.md) | 简体中文

# AppHopper

**Windows 上的应用级 `Alt+Tab`，外观 UI 来自 PowerToys Window Hopper**

两个快捷键, 职责完全分开:

| 快捷键 | 在什么之间切换 | 行为 |
|---|---|---|
| `Alt+Tab` | **应用** | 每个应用只占一个条目，按最近使用排序；前台应用的兄弟窗口不出现，所以 `Alt+Tab` 永远落在*另一个*应用上 |
| ``Alt+` `` | **前台应用的窗口** | 交给 [PowerToys Window Hopper](https://learn.microsoft.com/zh-cn/windows/powertoys/window-hopper)(或任何应用内切换器)处理 |

再也不用在五个资源管理器窗口之间"路过"才能到达浏览器：`Alt+Tab` 直接跳到下一个*应用*，``Alt+` `` 在当前应用内部循环

## 1.1.2beta1

- 修复启动失败时吞掉 `Alt+Tab`：投递失败直接放行，候选不足等启动失败通过带专用标记的 `SendInput` 回退；匹配的 `Tab` 松开会被消费，物理 `Alt` 松开始终放行。
- 如果按住 `Tab` 的过程中禁用切换器、进入提交或投递失败，已经放行的自动重复按下也会得到对应的松开，避免残留 Tab 状态。
- 支持首次 `Alt+Shift+Tab` 反向选择和对应分页；部分输入插入失败时，只清理确实由回放按下且尚未松开的键。
- 默认日志隐藏窗口标题和可执行文件名，`--log-verbose` 单独使用即可启用详细日志；按 UTF-8 字节限制完整记录，文件不超过 8 MiB。
- 移除 F24 授权按键注入及面向目标窗口的输入队列连接，最小化窗口异步还原；保留实际前台落定检查。
- 自启动检查安装路径、重解析点、文件及父目录 ACL；普通身份可修改的路径会被拒绝，失败时菜单不会假装启用成功。

这是测试版，未保证消除任务栏闪烁。回归测试覆盖输入、日志和自启动保护；隔离桌面的运行验证不能替代输入桌面上的快速切换、游戏及 UWP 实测。

## 1.1.2beta2

- 先保存原前台，再以零尺寸、无任务栏的可激活宿主领取前台；枚举和绘制在领取后进行。对当前前台窗口做 50ms 响应检查，短时连接其输入队列并激活本程序宿主，在 `finally` 中断开后才继续；不先做一次被拒绝的后台激活，不连接目标线程。
- 宿主与卡片层同时保持 WinForms/原生可见性一致；卡片层归属宿主，激活宿主后仍显示标题、卡片边框和选中框，不再只剩缩略图。
- 提交前要求宿主仍是实际前台，并通过 `AllowSetForegroundWindow` 对本进程预检。没有资格就不还原、不请求目标；有资格时异步还原最小化目标，仅请求一次 `SetForegroundWindow`，通过有界 `WM_NULL` 同步及实际前台检查判定落地。删除 `SwitchToThisWindow` 回退。
- `--log` 记录宿主领取、队列断开、权限预检、目标请求和只读 `HSHELL_FLASH` HWND。启动记录 `shell flash observer=True; activation=foreground-handoff`；观察器注册失败、日志已满或不可写时，不能用“无通知”判断无闪烁。
- Windows 10 输入桌面实测：Orca/ZCode 20 次系统输入模拟的 Alt+Tab，20 次均由 AppHopper 完成，无原生回退、目标请求均接受、Alt 均释放，目标 Shell 闪烁通知为 0，结束回到起始应用。此结果不覆盖所有应用、真实硬件输入、游戏或 UWP。

## 特性

- **真正的应用级 `Alt+Tab`** —— 窗口按进程分组,按 Z 序(最近使用)排序。每个组的代表窗口是其最近使用的窗口,因此切回某个应用时,你会回到上次离开的地方。
- **Window Hopper 风格 UI** —— 浮层完整移植自 PowerToys 的 `AltWindowCycle` 模块:WinUI 风格圆角卡片、每卡片图标+标题头、系统强调色双环选中框、跟随系统的深浅主题、带页码指示的分页,以及**不做背景变暗**。
- **DWM 实时缩略图** —— 与任务栏预览同机制的实时合成画面,按卡片比例居中裁剪,绝不拉伸变形。
- **完整的鼠标支持** —— 点击卡片直接切换、点击面板外任意位置取消、滚轮循环选择。(`Esc` 同样可以取消。)
- **虚拟桌面感知** —— 只列出*当前*桌面上的窗口(通过公开的 `IVirtualDesktopManager` 判断),切换永远不会把窗口拽到别的桌面。
- **低侵入** —— 不修改其他窗口的样式、归属或任务栏属性，只在切换时请求还原最小化目标；无法建立会话时，重放这次 `Tab` 交还给 Windows 原生切换器。
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

应用列表每次打开切换器时实时计算，没有任何需要配置的选项。诊断：以 `--log` 参数启动；日志最多 8 MiB，默认隐藏窗口标题和可执行文件名，仍保留 HWND、窗口类和时序。需要完整枚举细节时使用 `--log-verbose`，分享详细日志前请检查敏感内容。日志中的 `hotkey: alt+tab -> start`、`start aborted:` 和 `alt+tab fallback:` 用于区分消息到达、启动失败和主动回退；没有记录本身不能证明钩子未到达，日志也可能已满或不可写。

运行 `AppHopper.exe --self-test` 执行布局等纯逻辑自检。双击 `self-test.bat` 会把当前源码、`tests/RegressionTests.cs` 和 `tests/ActivationTests.cs` 编译到临时目录，运行输入状态、日志边界、ACL 及真实跨线程消息同步回归，再删除临时输出；同步回归使用非输入桌面的隐藏窗口，不替换运行中的 exe，不安装钩子，不改变前台，也不写入自启动注册表，退出码 0 表示通过。

`Start with Windows` 仅允许安装在 `Program Files` 或 `Program Files (x86)` 下的受保护路径；名称符合但 ACL 可被普通身份修改、存在重解析点的路径同样会被拒绝。HKCU Run 注册不绕过 UAC，Windows 可能阻止管理员程序在登录时启动，不能保证无人值守自启。

## 实现原理(简版)

- 键盘钩子仅在消息投递成功后消费 `Alt+Tab`，并消费匹配的 `Tab` 松开；启动失败通过带标记的输入回放交还原生切换器，物理 `Alt` 松开不被拦截。
- 顶层窗口按 Z 序枚举,经经典 Alt-Tab 资格规则 + 当前桌面检查过滤,再按进程映像路径分组(UWP 窗口通过其子 `Windows.UI.Core.CoreWindow` 归因到真实应用)。
- 实时预览是 `DwmRegisterThumbnail` 合成画面,渲染进一块不透明圆角面板;卡片层(标题、描边、选中环、页码)用 GDI+ 画进预乘 alpha 的 DIB,再经 `UpdateLayeredWindow` 合成——与其移植来源的 PowerToys 模块相同的双层设计。
- 激活以本程序前台宿主为交接点，不注入授权按键。只有领取自身前台时才短时连接当前前台线程，目标激活前已经断开；最小化目标经 `ShowWindowAsync` 异步还原。跨队列激活通过带超时的 `WM_NULL` 等待处理，再检查实际前台；参考[微软的异步激活说明](https://devblogs.microsoft.com/oldnewthing/20161118-00/?p=94745/)。50ms 响应检查和 200ms/300ms 同步、落定预算不是所有 Win32 调用的硬超时；检查后前台线程突然挂起仍存在队列连接竞态，也不能越过 Windows 前台权限限制。

## 致谢

- [PowerToys Window Hopper(`AltWindowCycle`)](https://github.com/microsoft/PowerToys) —— 浮层 UI 与布局移植自该模块(MIT)。欢迎给 PowerToys 点星,顺便看看其余的好东西。
- [alt-tab-macos](https://github.com/lwouis/alt-tab-macos) —— 应用级切换 + 预览的最初灵感来源。
- [window-switcher](https://github.com/sigoden/window-switcher) —— ``Alt+` `` 式应用内切换的先行者。

## 许可证

[MIT](LICENSE) © 2026 informalgit。源自 PowerToys 的 UI 代码仍受 MIT 许可约束，完整第三方声明见 [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt)。
