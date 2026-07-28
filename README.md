# Lingo

轻量级 Windows 剪贴板翻译工具。托盘常驻，按下全局快捷键即可翻译剪贴板中的文本，支持百度翻译与任意 OpenAI 兼容 API，两个服务并发请求、互不阻塞。

- 技术栈：C# / .NET 10 / WinForms / Windows x64
- 零轮询：空闲时不占 CPU，仅靠系统消息（RegisterHotKey）唤醒
- 无第三方依赖，仅使用 .NET 自带能力

## 功能

| 功能 | 说明 |
| --- | --- |
| 托盘常驻 | 无主窗口，托盘菜单：翻译剪贴板 / 设置 / 退出 |
| 全局快捷键 | 默认 `Ctrl+Alt+T`，可在设置中修改；被占用时气泡提示，不影响运行 |
| 翻译浮窗 | 深色主题、弹出时自动置顶一次、可拖动缩放（最小可缩到一行输入）、Esc 或关闭按钮隐藏（不退出）、记忆位置与大小、优先显示在鼠标所在屏幕；支持直接输入文本按 Enter 翻译（Shift+Enter 换行） |
| 百度翻译 | 通用文本翻译 API，需自备 AppID / SecretKey；目标为中文且原文已是中文时自动改译英文 |
| 模型翻译 | 任意 OpenAI Chat Completions 兼容服务（OpenAI / DeepSeek / Ollama 等），可自定义 Prompt（`{text}` 为原文占位符）与超时 |
| 文本预处理 | 自动将连字符 / 下划线还原为空格、拆分驼峰命名，适合翻译代码标识符 |
| 取消机制 | 连续触发翻译时自动取消上一轮未完成的请求 |

## 运行

环境要求：Windows x64（自包含发布无需安装 .NET 运行时）。

开发运行：

```powershell
dotnet run --project Lingo.csproj
```

启动后程序驻留托盘（不显示窗口）。复制任意文本，按 `Ctrl+Alt+T` 即弹出翻译窗口。

## 配置

首次使用请右键托盘图标 → 设置（标签页式深色界面）：

- **常规**：全局快捷键、目标语言、开机自启
- **百度翻译**：启用开关、App ID、Secret Key（申请地址：https://fanyi-api.baidu.com/ ）、源/目标语言
- **模型翻译**：启用开关、Endpoint（形如 `https://api.openai.com/v1/chat/completions`）、API 密钥、模型名、Prompt、超时秒数

配置保存在 `%LocalAppData%\Lingo\settings.json`；文件损坏时会自动备份为 `settings.corrupted.json` 并恢复默认。日志位于同目录 `lingo.log`（不记录任何密钥）。

## 发布

```powershell
powershell -ExecutionPolicy Bypass -File publish.ps1
```

或手动执行：

```powershell
dotnet publish Lingo.csproj -c Release /p:PublishProfile=SelfContained
```

产物为单文件 `artifacts\self-contained\Lingo.exe`（Release / win-x64 / 自包含 / 压缩单文件），拷贝到任意机器即可运行。

## 项目结构

```
Lingo/
├── Forms/            # TranslateForm 翻译浮窗、SettingsForm 设置、ResultPanel 结果面板
├── Services/         # ITranslator、BaiduTranslator、CustomApiTranslator、
│                     # TranslationService（并发调度）、HotkeyService、
│                     # ClipboardService、TrayService、StartupService
├── Models/           # AppSettings、TranslationResult
├── Infrastructure/   # SettingsStore、AppLogger、AppIcon
├── Assets/           # 图标
├── Program.cs        # 单实例 + 全局异常处理入口
└── TrayApplicationContext.cs  # 托盘常驻上下文，组装各服务
```
