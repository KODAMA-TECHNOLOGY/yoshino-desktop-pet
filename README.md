# 芳乃桌宠 · Yoshino Desktop Pet

[![许可证](https://img.shields.io/badge/license-GPL--3.0-blue.svg)](LICENSE)
[![版本](https://img.shields.io/badge/version-1.3.1-green.svg)](https://github.com/KODAMA-TECHNOLOGY/yoshino-desktop-pet/releases/latest)

Windows 上的独立芳乃桌宠：用高清 Q 版动画显示本机 Codex 的工作状态、最近动作和额度，并提供原生 Codex 窗口的快捷入口。当前发布版本为 **1.3.1 高清转头版**。

**由 OpenAI Codex（AI 编程助手）根据 KODAMA-TECHNOLOGY 的需求制作。** 主要实现、调试和文档由 Codex 完成，详细署名见 [AUTHORS.md](AUTHORS.md)。

## 下载与运行

到 [Releases](https://github.com/KODAMA-TECHNOLOGY/yoshino-desktop-pet/releases/latest) 下载 Windows x64 压缩包，完整解压后运行 `YoshinoPet.exe`。图片资源必须与程序一起保留。

运行环境：Windows 10/11 x64、.NET Framework 4.8、已登录的 Codex CLI，以及能够处理 `codex://` 链接的 Codex 桌面应用。程序依赖本机 Codex 的接口和活动记录，未来的 Codex 更新可能影响兼容性。

## 主要功能

- 九种状态动画：待机、左右移动、招手、跳跃、失败、等待、工作和完成。
- 八方向高清完整人物图随鼠标切换视线；鼠标静止约 3 秒后恢复任务动画。
- 检查本机 Codex 活动记录，显示命令执行、文件修改、等待输入及任务完成等可识别动作。
- 最近动作气泡保留最近 3 条，约 6 秒后自动收起；点击可返回对应会话。
- 悬停显示套餐额度、额外 credits、重置时间及缓存时间。
- 快捷打开原生 Codex 新建输入框，或截图后复制并尝试粘贴到输入框，由用户确认发送。
- 拖动定位、大小调节、全屏自动隐藏、托盘菜单和可选开机启动。

高清动画使用关键姿态和方向图，并非每个动作的每一帧都独立重绘。示例图组如下：

<img src="assets/hq/face.png" alt="芳乃高清姿态图组" width="420" />

## 常用操作

| 操作 | 默认方式 |
| --- | --- |
| 打开 Codex 新建输入框 | 点击芳乃，或 `Ctrl+Alt+Space` |
| 截图并打开新建输入框 | `Ctrl+Alt+S` |
| 显示 / 隐藏桌宠 | `Ctrl+Alt+Y` |
| 移动桌宠 | 左键拖动 |
| 设置 / 退出 | 右键桌宠或系统托盘 |
| 查看额度 | 鼠标悬停 |

快捷键的最后一个键可在设置中调整。若与其他软件冲突，请修改后重试。

## 状态、数据与边界

状态通常约每秒检查一次，额度约每 60 秒刷新。这是对本机记录和接口的读取，更新速度还受记录落盘与接口响应影响。无法解析的信息会显示可确认部分；缺失的额度为“未知”，刷新失败时保留带时间的缓存。

配置、日志和截图保存在 `%LOCALAPPDATA%\YoshinoPet`，Codex 登录及历史使用现有 `CODEX_HOME`。本版本通过 App Server 查询状态与额度；对话和任务操作在 Codex 原生窗口进行。截图会覆盖剪贴板，不会自动发送；无法自动粘贴时，可在原生输入框手动 `Ctrl+V`。

仓库和下载包不包含个人 Codex 会话、登录凭据、截图或运行日志。程序产生的本地数据可能包含工作内容，请按个人需要保管和清理。

## 从源码构建

源码无需 NuGet 包，使用 Windows 的 .NET Framework C# 编译器：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\build\YoshinoPet.exe --self-test .\test-results
```

构建结果为 `build\YoshinoPet.exe`，同目录还会生成配置文件与 `assets`。分发时需完整保留。

可选验证命令：

```powershell
.\build\YoshinoPet.exe --integration-test .\test-results
.\build\YoshinoPet.exe --ui-test .\test-results
```

集成测试需要现有 Codex 登录；UI 测试会操作受控窗口和剪贴板，运行前先退出正式桌宠，避免快捷键冲突。已有验证范围及限制见 [验证说明](验证说明.md)。

## 源码结构

| 路径 | 内容 |
| --- | --- |
| `src/` | 桌宠、状态监测、Codex 客户端、设置及测试 |
| `assets/hq/` | 实际使用的 AI 重绘高清图组 |
| `assets/yoshino-v2.png` | 兼容精灵图 |
| `build.ps1` | Windows 构建脚本 |
| `docs/使用说明.md` | 详细使用和行为说明 |

## 许可与反馈

原创源码和文档采用 [GNU GPL v3.0](LICENSE)，SPDX 标识为 `GPL-3.0-only`。角色及相关作品名称的既有权利仍属于原权利人；AI 重绘素材与程序用途不构成官方授权或背书。图组的生成方式见 `绘图提示词.txt`。

如需报告问题，请在 Issues 中提供 Windows 版本、程序版本、复现步骤及脱敏后的错误信息；不要上传登录文件、完整会话日志或私密截图。

本次公开发布的检查结果见 [发布验证](docs/发布验证.md)。
