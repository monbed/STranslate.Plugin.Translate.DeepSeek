# STranslate.Plugin.Translate.DeepSeek

适用于 [STranslate](https://github.com/STranslate/STranslate) 的 DeepSeek 翻译插件，通过 DeepSeek 的 Chat Completions 接口完成翻译、润色和总结，也可以接入兼容该接口的第三方服务。

## 功能

- 流式输出译文
- 思考模式与推理强度（low / high / max）
- 一键从接口获取可用模型，也可以手动添加和删除模型
- 自定义提示词，内置翻译、润色、总结三个模板
- 接口地址自动补全，支持官方地址和第三方中转地址
- 账户余额查询
- 连接失败、超时、限流（429）和服务端错误（5xx）时自动重试
- 译文被截断、被过滤或生成被中断时给出明确提示
- 界面语言：简体中文、繁體中文、English、日本語、한국어、Türkçe、Русский、Українська、فارسی

## 安装

1. 在 [Releases](https://github.com/monbed/STranslate.Plugin.Translate.DeepSeek/releases) 页面下载最新的 `.spkg` 插件包
2. 打开 STranslate，进入 设置 > 插件，将下载的文件拖入窗口即可安装

建议使用 STranslate 2.0.10 或更高版本。

## 配置

1. 在 STranslate 中添加 DeepSeek 翻译服务
2. 填写 API 密钥，可在 [DeepSeek 开放平台](https://platform.deepseek.com/api_keys) 创建
3. 选择模型，或点击“获取模型”从接口读取可用模型
4. 点击“验证”确认配置可用

### 接口地址

| 填写内容 | 实际请求地址 |
| --- | --- |
| 留空 | `https://api.deepseek.com/chat/completions` |
| `https://api.deepseek.com` | `https://api.deepseek.com/chat/completions` |
| `https://example.com/v1` | `https://example.com/v1/chat/completions` |
| `https://example.com/v1/chat/completions` | 原样使用 |
| `https://example.com/api/chat#` | `https://example.com/api/chat` |

- 地址不以 `/chat/completions` 结尾时会自动补上
- 以 `#` 结尾表示按填写的地址原样请求，不做补全
- 输入框下方会显示实际请求地址
- “获取模型”请求与聊天接口同级的 `/models`，例如 `https://example.com/v1/models`
- 余额查询请求站点根路径下的 `/user/balance`，这是 DeepSeek 官方接口，第三方服务不一定支持

### 模型参数

| 参数 | 说明 |
| --- | --- |
| 思考模式 | 开启后模型先推理再作答，默认开启 |
| 推理强度 | low / high / max，默认 high，仅在思考模式开启时生效 |
| 温度 | 控制输出的随机性，仅在思考模式关闭时生效 |
| Max Tokens | 限制单次请求生成的最大 Token 数，默认关闭。关闭时不发送该参数，由服务端决定 |

思考模式开启时，如果 Max Tokens 数值过小，译文可能被截断或为空，此时插件会提示调大或关闭 Max Tokens。

### 提示词

提示词中可以使用以下占位符：

| 占位符 | 含义 |
| --- | --- |
| `$source` | 源语言 |
| `$target` | 目标语言 |
| `$content` | 待处理的文本 |

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```powershell
dotnet build .\STranslate.Plugin.Translate.DeepSeek\STranslate.Plugin.Translate.DeepSeek.csproj --configuration Release
```

构建完成后，插件包位于 `.artifacts/plugins` 目录。

## 贡献

欢迎提交 Issue 和 Pull Request。

## 相关链接

- [STranslate](https://github.com/STranslate/STranslate)
- [DeepSeek API 文档](https://api-docs.deepseek.com/zh-cn/)
- [DeepSeek 开放平台](https://platform.deepseek.com/)

## 许可证

[MIT](LICENSE)
