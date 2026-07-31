# Codex worklog

## 2026-07-31 PR #1 review and correction

- 目标：审核 `agent/enhance-connection-status-ui` 相对 `main` 的功能、安全边界、构建与分发一致性；修正阻断项后再决定是否合并。
- 审核发现：
  - 静默状态刷新期间恢复按钮仍可发起配置变更，存在只读检查与切换操作并发的风险。
  - 成功但带警告的切换会清除 `Degraded` / `Orphaned` 的恢复按钮。
  - 任意未知命令行参数会委托到旧入口并显示旧界面。
  - README 指向的已提交 EXE 仍为旧版本，源码与分发产物不一致。
- 修正：
  - 状态刷新期间禁用并二次拦截所有配置变更入口，操作结束后恢复自动刷新。
  - 保留异常状态的上下文恢复操作，增加空操作结果防御。
  - 仅将已知只读命令行参数委托给旧入口。
  - 为外部程序启动增加内联错误处理，收紧用户目录脱敏边界。
  - 增加交互防并发自检与已提交 EXE 防陈旧检查，并重新构建 `dist\CodexRouterSwitch.exe`。
- 本地验证：
  - .NET Framework C# 编译通过。
  - 增强 GUI 无窗口自检通过；未执行配置变更。
  - PowerShell 语法解析通过。
  - 完整集成测试未运行：本机默认位置缺少上游 `codex-router` 安装，测试在只读前提检查阶段退出，真实 Codex 配置未改变。
- GitHub 验证：Windows Actions 已通过已提交 EXE 校验、源码重新编译和增强 GUI 无窗口自检；该工作流作为合并门。

## 2026-07-31 v1.2 现代中文界面

- 目标：依据用户选定的左右分栏方案重做整体界面，消除旧式 WinForms/WinXP 观感；
  所有应用自有界面文案使用中文，保留现有路由切换安全逻辑。
- 设计与实现：
  - 新增 `src/ModernUiControls.cs`，使用 Windows 自带的
    `Microsoft YaHei UI` 和 `Segoe MDL2 Assets`，实现圆角按钮、现代模式项、
    图标与分隔线，无第三方 UI 依赖。
  - 主窗口改为自绘标题栏、左侧连接方式选择、右侧状态事实区、上下文恢复面板和
    固定底部命令栏。
  - 正常、原生、降级、未跟踪进程、错误、处理中和重启提醒状态全部中文化；
    未跟踪进程明确显示“运行中（未纳入管理）”。
  - 模型与服务商显示增加友好化处理，例如
    `deepseek/deepseek-v4` 显示为 `DeepSeek V4`，`gpt-5` 显示为 `GPT-5`。
  - 剪贴板重启步骤、诊断报告、辅助功能名称和应用自有错误提示改为中文。
  - 程序与清单版本更新为 `1.2.0.0`，重新构建并提交
    `dist/CodexRouterSwitch.exe`。
- 保持不变：
  - `RouterController` 的配置切换顺序、PID/命令行归属验证、失败回滚和
    “不终止未知进程”边界未改变。
  - 程序仍不会自动退出或重启 Codex，也不会读取或保存供应商密钥。
- 本地验证：
  - .NET Framework C# 编译通过，编译警告级别保持 `/warn:4`。
  - GUI 无窗口自检通过：`modernUi`、`pureChineseUi`、`layoutSafe`、
    `refreshMutationGuard` 和 `legacyArgsRestricted` 均为 `true`，
    未显示窗口、未执行配置变更。
  - 只读夹具捕获了 8 种界面状态；默认 `820 × 760` 和最小
    `800 × 720` 下没有裁切或重叠，异常面板与重启提醒同时显示仍可用。
  - `design-qa.md` 对所选设计与最终截图进行同屏比较，结论为 `passed`。
  - `Test-CodexRouterSwitch.ps1` 的完整路由集成部分未能在本机运行：默认位置缺少
    上游 `codex-router\src\config-manager.mjs`。没有为测试下载或安装上游项目，
    真实 Codex 配置未改变。
- GitHub 验证：Windows Actions 已通过已提交 EXE、程序集版本、现代中文 UI 自检和
  源码重新编译；PR 当前可合并，保持 Draft 等待人工审阅。
