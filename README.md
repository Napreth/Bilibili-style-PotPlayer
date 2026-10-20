# Bilibili-style PotPlayer

**开发阶段：pre-alpha。** 面向 Windows 64 位 PotPlayer 的哔哩哔哩风格皮肤与辅助程序，目前仍在迭代，功能和配置格式可能变化。

项目保留 PotPlayer 的播放、解码和渲染能力，调整控制栏、键鼠交互、轨道选择及字幕／弹幕界面。它不是哔哩哔哩官方项目，也不提供 B 站账号、在线点播或缓存下载功能。

## 当前功能

- **精简控制栏**：左侧为上一项、播放／暂停、下一项和时间；右侧为音频参数、视频参数、倍速、字幕和弹幕、音量、全屏。
- **悬浮控制**：音量按钮单击静音，悬停调音量；倍速菜单提供 0.25×、0.5×、1×、2×、3×、5×、10×，1× 时按钮显示“倍速”。
- **音视频信息与选轨**：显示 Hi-Res、空间音频、声道布局、HDR、分辨率和帧率，详细规格保留在悬浮列表中。单轨时不弹出列表，打开文件时沿用 PotPlayer 的自动选轨结果。Hi-Res 标记描述文件规格，不评价实际听感。
- **字幕和弹幕**：汇总同目录文件、手动导入文件和容器内文本软字幕；支持主／副双字幕、普通弹幕独立开关、内容分类、拖动改分类和分页列表。
- **样式设置**：字体预览、字号、粗体／斜体、文字颜色、背景填充、描边和投影；弹幕另有类型过滤、显示区域、透明度、速度、随屏幕缩放和防挡字幕。设置与手动分类自动保存。
- **播放列表内缩**：窗口和全屏模式下均在内部缩小视频区域，保持外窗尺寸和鼠标位置；空间不足时暂时收起，放大后恢复原来的展开状态。全屏右边缘悬停不展开侧栏。
- **悬浮控制栏与小窗模式**：正常模式的半透明底栏在鼠标进入播放区时显示。小窗模式提供悬浮标题栏、精简底栏、字幕／弹幕开关及视频比例锁定，鼠标移出后只保留画面。
- **360° 操作**：隐藏四个固定预览视角，按住中键拖动视角，单击中键回正。

控制栏嵌入播放器原生窗口，跟随最大化、恢复尺寸、全屏和 DPI 变化；窗口模式下鼠标移出播放区即隐藏，全屏下鼠标静止 3 秒后隐藏、移动后恢复。操作悬浮菜单时保持显示，悬浮菜单和快捷键只对获得焦点的播放器启用。

## 环境要求

- Windows 64 位与 64 位 PotPlayer。
- .NET Framework 及其 64 位 C# 编译器。构建脚本使用 Windows 自带的 `csc.exe`，不需要安装 .NET SDK。
- Windows PowerShell 5.1 或 PowerShell 7。
- 读取容器内文本字幕需要 **FFmpeg**，仓库不附带 FFmpeg。辅助程序依次从自己的安装目录、当前用户 WinGet 链接目录和 `PATH` 查找 `ffmpeg.exe`。

MediaInfo DLL 及其授权文件包含在 `plugin/InputBridge/vendor/` 中；窗口布局模块 `PlayerLayout.dll` 及 C 源码位于 `plugin/InputBridge/native/`。皮肤的播放控制栏需要同时安装辅助程序。

## 安装

先关闭所有 PotPlayer 窗口，在项目根目录打开 PowerShell。将下面的目录改为自己的 PotPlayer 安装目录，并确保具有该目录的写入权限：

```powershell
$playerDir = 'C:\Program Files\DAUM\PotPlayer'

.\skin\build.ps1
Copy-Item .\skin\BilibiliLite.dsf (Join-Path $playerDir 'Skins\BilibiliLite.dsf') -Force
.\settings\install-input.ps1 -PlayerPath (Join-Path $playerDir 'PotPlayerMini64.exe')
```

启动 PotPlayer，在“皮肤”菜单选择 `BilibiliLite`。

安装脚本会编译并检查辅助程序，备份将修改的原生设置，再应用本项目的鼠标和快捷键配置。原生主窗口的自定义快捷键会被清空，原值一并保存在 `settings/.local/original-input.json`。请保留这份备份，重复安装会沿用它。

辅助程序安装到 PotPlayer 的 `Extension/BilibiliInput/`，由播放扩展随 PotPlayer 启动，最后一个播放器实例退出后自动结束，无登录自启。辅助程序读取媒体规格和字幕；点击“在线字幕搜索”才会打开 PotPlayer 的原生搜索入口。

运行中更新及完整安装细节见 [操作配置与安装说明](settings/README.md)。

## 常用操作

| 操作 | 功能 |
| --- | --- |
| → / ← 短按 | 前进 / 后退 5 秒 |
| → 长按 | 按住期间 2 倍速，松开恢复原速度 |
| ← 长按 | 连续后退 |
| ↑ / ↓、鼠标滚轮 | 调整音量，每次 10 个百分点 |
| Space、视频区域左键单击 | 暂停 / 播放 |
| F、视频区域左键双击 | 进入 / 退出全屏 |
| Esc | 退出全屏 |
| `,` / `[`、`.` / `]` | 上一项、下一项 |
| M | 静音 / 取消静音 |
| Shift+1 / Shift+2 | 1 倍速 / 2 倍速 |
| D | 普通弹幕总开关 |
| C | 字幕总开关，保留主／副字幕选择 |
| S | 切换小窗／正常模式 |
| X | 打开 / 收起播放列表 |
| 360° 视频中键拖动 / 单击 | 转动视角 / 回正 |
| F1–F12（F5 除外） | 保留原生功能 |

F5 保留给后续自定义设置页，Enter 当前无动作。右键菜单暂时禁用，未列出的播放器快捷键由辅助程序屏蔽；系统窗口操作和原生菜单／对话框输入不受影响。完整按键表见 [settings/README.md](settings/README.md)。

## 字幕与弹幕

按内容识别 SRT、ASS/SSA、WebVTT、LRC、TTML、B 站 XML 及约定结构的 JSON，扩展名不单独决定用途。用途不明的文本进入“分类存疑”，可以拖到对应分类；混有普通和高级弹幕的来源会拆开列出。

单字幕模式再次点击选中项即可关闭。开启多语言字幕后最多选择两条，分别标为主／副；先填空位，满两条时替换副字幕，关闭主字幕会将副字幕提升为主字幕。

普通弹幕支持滚动、反向滚动、顶部及底部固定。主菜单提供显示区域、不透明度和速度，字号移入螺母设置页。默认保留弹幕原色，也可指定统一文字颜色；关闭“启用彩色弹幕”会强制渲染为白色，与按原始颜色过滤弹幕相互独立。

设置保存在 `%LOCALAPPDATA%\BilibiliPotPlayer\caption-options.json`，包含显示样式和手动分类，不改写源字幕或弹幕文件。

## pre-alpha 限制

- 高级、代码和 BAS 弹幕仅识别、分类与计数，暂不播放或执行。
- ASS 文本使用统一样式，不复现原有动画、绘图及复杂排版。
- PGS、VobSub 等图像字幕沿用 PotPlayer 原生渲染，面板会列出，但暂不支持在此切换。
- 翻译、语音生成字幕、自定义右键菜单和 F5 设置页尚未实现；智能识别人像的防挡弹幕尚未实现。
- Hi-Res、HDR、空间音频标记基于源文件信息，不表示当前设备或渲染链路已经完整输出对应效果。
- 窗口低于 **1024×600 逻辑像素**时暂时关闭并禁止展开侧栏，正常控制栏保持一致；低于 **800×480** 时进入小窗模式。这些数值按本皮肤控件空间制定，并非 B 站官方阈值。S 默认使用约 640 像素宽的视频窗口，返回时恢复至允许侧栏的最小尺寸，并按记忆状态展开侧栏。
- 触控布局保留，但尚未像常规桌面布局一样完整验证；不同 PotPlayer 版本、渲染器和多屏 DPI 组合仍需进一步验证。

## 恢复原生操作配置

关闭 PotPlayer，在项目根目录执行：

```powershell
.\settings\restore-input.ps1
```

脚本恢复首次安装前备份的交互设置及自定义快捷键，并移除本项目的启动扩展。皮肤需要在 PotPlayer 中自行切回，字幕／弹幕样式配置保留在用户目录。

## 构建与验证

```powershell
# 重新打包皮肤
.\skin\build.ps1

# 编译辅助程序并运行检查
.\plugin\InputBridge\build.ps1 -Test

# 修改 C 源码后重建窗口布局模块（需要 64 位 MinGW-w64 GCC）
.\plugin\InputBridge\native\build.ps1

# 只读查看当前播放器状态
.\plugin\InputBridge\bin\InputBridgeTests.exe --status
```

检查程序覆盖输入操作、媒体元数据、字幕解析、设置持久化和控件布局。`-Test` 运行逻辑检查；窗口布局检查通过独立命令执行。

`--window-check` 会临时改变测试播放器尺寸，检查侧栏开关、状态记忆及 C/S 快捷键，结束后恢复窗口状态。`PlayerLayout.dll` 更新后需要重新启动 PotPlayer。更多诊断命令见 [验证说明](settings/README.md#验证与接口来源)。

## 目录

```text
skin/                  皮肤源文件、打包脚本与 BilibiliLite.dsf
plugin/InputBridge/    C# 辅助程序、启动扩展和第三方依赖
settings/              操作配置、安装／恢复脚本和详细说明
tests/                 检查程序与小型元数据样本
```

配置备份、编译输出及开发验证产物不进入 Git。

## 授权与资源来源

本项目原创代码采用 [MIT License](LICENSE)。皮肤基于 PotPlayer 随附的默认皮肤修改，原始皮肤资源不属于本项目原创内容；Dolby 标志、哔哩哔哩播放器图标及相关商标归各自权利人所有，不包含在原创代码的 MIT 授权范围内。

- [MediaInfo 授权文件](plugin/InputBridge/vendor/MediaInfo.License.html)
- [杜比 SVG 资源来源](plugin/InputBridge/assets/README.md)
- [弹幕过滤图标来源](plugin/InputBridge/assets/BilibiliFilterSources.md)
- [测试样本说明](tests/fixtures/README.md)
