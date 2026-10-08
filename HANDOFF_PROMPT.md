# 接力提示词：AppHopper 1.3

这是 Windows 应用级 Alt+Tab 切换器：单文件 `AppHopper.cs`、Windows 自带 .NET Framework `csc.exe` 构建、托盘常驻、PowerToys Window Hopper 风格双层浮层。

## 当前版本与架构

- 当前源码信息版本 `1.3`，程序集、文件及 Manifest 数值版本 `1.3.0.0`，对应发布标签 `v1.3`；用户已批准简短发布文案并授权提交、推送、tag、GitHub Release 和已验证 exe 附件。
- `PanelForm`、`ChromeForm`、`MsgForm` 使用 `WS_EX_NOACTIVATE`。显示、刷新、取消不主动请求前台；低级键盘和鼠标钩子向消息窗投递操作。
- `Commit` 先关闭会话触发并捕获目标，再隐藏浮层，最后激活目标。`ForceForeground` 短时连接当前前台线程及不同的目标线程，执行 `SetFocus(保存的目标焦点) → BringWindowToTop → SetForegroundWindow`，在 `finally` 中逆序断开后才泵消息和做有界落定检查。反向顺序在控制台源、长按选择时复现拒绝，短按 MRU 测试不会暴露此问题；先恢复焦点后通过相同表面验证。
- Window Hopper 切同应用窗口，我们切跨应用窗口，不能假设源和目标队列相同。连接前用 `GetGUIThreadInfo` 保存目标子控件焦点，仅恢复属于选中窗口或其子控件的焦点，否则以目标顶层窗口作为焦点入口。必须验证实际文本输入，而不是仅检查前台 HWND。受控的两个独立应用已通过 20 次切换与目标编辑框实际输入，以及最小化目标还原后的输入验证；探针注入需真实扫描码，不能把零扫描码注入的结果当作物理键盘证据。
- 激活前通过有界 `WM_NULL` 同步源输入窗口；UWP 源使用 CoreWindow。现场 UWP 队列连接返回 `ERROR_ACCESS_DENIED`，但 `SetForegroundWindow` 仍能成功，不能把连接失败当成切换否决。连接成功的队列才在 `finally` 中断开。
- 已删除 `ClaimSessionForeground`、`TryClaimFromForegroundQueue`、`SessionOwnsForeground`、`ForceForegroundViaHandoff`、`AllowSetForegroundWindow`、延迟取消归还及旧诊断结构。取消/放弃会话只隐藏、释放会话资源，不重新激活源窗口。旧 beta 版本关于必须领取宿主前台、保留失败授权调用的要求不适用于此架构。
- 必须同步 `_panel.Show()` / `_chrome.Show()` 的托管可见性。仅用原生 `SetWindowPos` 显示会让 WinForms `Hide()` 可能失效。
- 保留应用级分组、Z-order MRU、分页、当前虚拟桌面过滤、UWP 代表窗口、DWM 缩略图、Foxmail 无标题窗口资格、异步最小化还原。这不同于 Window Hopper 的同应用窗口选择。
- 托盘 `Get updates...` 按需读取 GitHub 最新正式版，确认后后台下载并校验 SHA-256、大小及文件版本；检查、确认和下载期间禁止重复操作。临时复制当前 exe 作为助手，在受保护的 ProgramData 目录运行，校验父进程身份及候选文件，锁住目标目录链；其他盘符安装先创建并锁住同卷受保护暂存目录。确认准备成功后才退出旧进程，用 `File.Replace` 原位替换并等待新版启动信号；失败尝试恢复并启动旧版，不修改 Run 项。
- `--apply-update`、`--update-started` 为更新内部协议，不是常规用户启动参数；助手先于单实例互斥量运行，新版在托盘和钩子初始化后报告启动完成。旧版 1.2 需手动安装一次带更新功能的版本。临时助手安排 Windows 重启时删除。

## 输入及异常约束

- `WH_KEYBOARD_LL` 静态委托保持存活，主线程消息窗接收 `WM_APP_START/NEXT/PREV/COMMIT/CANCEL/COMMITAT`。非激活浮层不需要额外 UI 线程才能接收 `PostMessage`。
- 仅成功投递时吞 Tab；匹配释放及自动重复放行规则保持原样。物理 Alt 松开始终放行；启动失败以带专用标记的 `SendInput` 回退原生切换。
- `Commit` / `Cancel` / `AbortSession` 保留重入保护；消息链和枚举回调捕获异常；鼠标提交索引检查范围。
- 跨线程 Win32 调用可能阻塞。200ms/300ms 同步预算不是整个激活链的硬超时，不能保证挂起应用或 Windows 前台限制下成功。

## 验证与历史证据

- `self-test.bat` 编译临时输出，当前 23 项回归，新增更新版本比较和不安全附件元数据拒绝。显示回归短暂显示非激活窗口并检查显示/隐藏不改变前台；自检不安装钩子、不写自启动注册表。
- 1.3 更新验证：真实 GitHub 1.2 发布元数据、下载和 SHA-256 校验通过；最终构建的成功替换、篡改拒绝、新版启动失败回滚、目标文件占用恢复均通过，普通盘符及临时 `R:` 映射各一轮，`SMOKE failures=0`。跨盘符分支确认退出旧进程前已准备同卷候选文件，结束后无残留备份和同卷暂存目录。原始输出为 `ab/update-final-same-volume.out`、`ab/update-final-cross-drive.out`、`ab/update-live.out`；机器只有 C 卷，盘符映射不是实际第二卷测试。
- 1.2 改造已在输入桌面验证双层浮层、保持源前台、Alt 按住时 Esc 取消、外部点击、反向选择、Tab/滚轮选择和卡片点击。截图确认缩略图、标题及选中框。
- 不允许把成功提交等同于无闪烁，也不允许把 `WS_EX_NOACTIVATE` 描述为禁止显式 `SetForegroundWindow`。此前“零闪烁只能以失败换取”等推断不成立。
- 最终构建交替 A/B（每版两轮）：beta8 39 会话、0 失败、23 次会话内闪烁；1.2 40 会话、0 失败、0 次会话内闪烁。原始输出在本机临时目录 `ab/check12-final.out`；统计排除会话外测试脚本抬升窗口造成的闪烁。
- 最终构建受控输入验证 20 次全部命中目标并让编辑框收到文本；最小化目标还原后也收到输入。浮层交互验证含长按反向、Tab/滚轮、卡片点击、Alt 按住时 Esc、外部取消及 10 次快速切换，`errors=0`。原始输出为 `ab/input12.out` / `ab/surface12.out`。这不覆盖所有真实硬件输入、游戏、UWP 场景、挂起目标和混合 DPI 多显示器行为，不能承诺所有环境零失败或零闪烁。

## 构建与诊断

- 当前环境可以使用 Windows `csc.exe`，不要沿用旧沙箱禁止编译的结论。
- 普通 `--log` 默认脱敏、UTF-8 完整记录最多 8 MiB；逐窗口枚举只在 `--log-verbose` 输出。不得用没有日志记录推断钩子没收到按键。
- 自启动要求 Program Files 内受保护路径及 ACL；不放宽检查或修改系统 ACL 伪造通过。HKCU Run 不绕过 UAC。
- 历史快照在 Git；日期记忆是历史证据，不是现行设计约束。

## 提交约定

- 按项目既有约定，修复验证完成后更新版本与发布说明并本地提交；未经推送授权不执行 push。
- “推送 / 推吧”按原约定包含提交、push、版本 tag、GitHub Release 和已验证 exe 附件。用户已认可 1.2、1.3 简短文案并授权发布；issue #1 由用户自行回复，不代发评论、不关闭 issue。
- Release 文档长期格式和简洁要求见根目录 `AGENTS.md`：仅 `bug fix`、`features` 两节及项目符号；相关条目注明 issue 编号并 @提出人。
