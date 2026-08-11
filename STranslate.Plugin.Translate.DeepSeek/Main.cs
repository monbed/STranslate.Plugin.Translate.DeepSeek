using Microsoft.Extensions.Logging;
using STranslate.Plugin.Translate.DeepSeek.View;
using STranslate.Plugin.Translate.DeepSeek.ViewModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Controls;

namespace STranslate.Plugin.Translate.DeepSeek;

public class Main : LlmTranslatePluginBase
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

    public override void SelectPrompt(Prompt? prompt)
    {
        base.SelectPrompt(prompt);

        // 保存到配置
        Settings.Prompts = [.. Prompts.Select(p => p.Clone())];
        Context.SaveSettingStorage<Settings>();
    }

    public override Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings, this);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public override string? GetSourceLanguage(LangEnum langEnum) => langEnum switch
    {
        LangEnum.Auto => "Requires you to identify automatically",
        LangEnum.ChineseSimplified => "Simplified Chinese",
        LangEnum.ChineseTraditional => "Traditional Chinese",
        LangEnum.Cantonese => "Cantonese",
        LangEnum.English => "English",
        LangEnum.Japanese => "Japanese",
        LangEnum.Korean => "Korean",
        LangEnum.French => "French",
        LangEnum.Spanish => "Spanish",
        LangEnum.Russian => "Russian",
        LangEnum.German => "German",
        LangEnum.Italian => "Italian",
        LangEnum.Turkish => "Turkish",
        LangEnum.PortuguesePortugal => "Portuguese",
        LangEnum.PortugueseBrazil => "Portuguese",
        LangEnum.Vietnamese => "Vietnamese",
        LangEnum.Indonesian => "Indonesian",
        LangEnum.Thai => "Thai",
        LangEnum.Malay => "Malay",
        LangEnum.Arabic => "Arabic",
        LangEnum.Hindi => "Hindi",
        LangEnum.MongolianCyrillic => "Mongolian",
        LangEnum.MongolianTraditional => "Mongolian",
        LangEnum.Khmer => "Central Khmer",
        LangEnum.NorwegianBokmal => "Norwegian Bokmål",
        LangEnum.NorwegianNynorsk => "Norwegian Nynorsk",
        LangEnum.Persian => "Persian",
        LangEnum.Swedish => "Swedish",
        LangEnum.Polish => "Polish",
        LangEnum.Dutch => "Dutch",
        LangEnum.Ukrainian => "Ukrainian",
        LangEnum.Uzbek => "Uzbek",
        _ => "Requires you to identify automatically"
    };

    public override string? GetTargetLanguage(LangEnum langEnum) => langEnum switch
    {
        LangEnum.Auto => "Requires you to identify automatically",
        LangEnum.ChineseSimplified => "Simplified Chinese",
        LangEnum.ChineseTraditional => "Traditional Chinese",
        LangEnum.Cantonese => "Cantonese",
        LangEnum.English => "English",
        LangEnum.Japanese => "Japanese",
        LangEnum.Korean => "Korean",
        LangEnum.French => "French",
        LangEnum.Spanish => "Spanish",
        LangEnum.Russian => "Russian",
        LangEnum.German => "German",
        LangEnum.Italian => "Italian",
        LangEnum.Turkish => "Turkish",
        LangEnum.PortuguesePortugal => "Portuguese",
        LangEnum.PortugueseBrazil => "Portuguese",
        LangEnum.Vietnamese => "Vietnamese",
        LangEnum.Indonesian => "Indonesian",
        LangEnum.Thai => "Thai",
        LangEnum.Malay => "Malay",
        LangEnum.Arabic => "Arabic",
        LangEnum.Hindi => "Hindi",
        LangEnum.MongolianCyrillic => "Mongolian",
        LangEnum.MongolianTraditional => "Mongolian",
        LangEnum.Khmer => "Central Khmer",
        LangEnum.NorwegianBokmal => "Norwegian Bokmål",
        LangEnum.NorwegianNynorsk => "Norwegian Nynorsk",
        LangEnum.Persian => "Persian",
        LangEnum.Swedish => "Swedish",
        LangEnum.Polish => "Polish",
        LangEnum.Dutch => "Dutch",
        LangEnum.Ukrainian => "Ukrainian",
        LangEnum.Uzbek => "Uzbek",
        _ => "Requires you to identify automatically"
    };

    public override void Init(IPluginContext context)
    {
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();

        Settings.Prompts.ForEach(Prompts.Add);
    }

    public override void Dispose() => _viewModel?.Dispose();

    public override async Task TranslateAsync(TranslateRequest request, TranslateResult result, CancellationToken cancellationToken = default)
    {
        if (GetSourceLanguage(request.SourceLang) is not string sourceStr)
        {
            result.Fail(Context.GetTranslation("UnsupportedSourceLang"));
            return;
        }
        if (GetTargetLanguage(request.TargetLang) is not string targetStr)
        {
            result.Fail(Context.GetTranslation("UnsupportedTargetLang"));
            return;
        }

        var messages = BuildMessages(sourceStr, targetStr, request.Text);

        // 瞬时故障（连接失败/超时/429/5xx）按配置重试；MaxRetries 为 null 或 <= 0 时不重试
        var maxAttempts = Math.Max(0, Settings.MaxRetries ?? 0) + 1;
        var retryDelay = Math.Max(0, Settings.RetryDelayMilliseconds);

        for (var attempt = 1; ; attempt++)
        {
            if (attempt > 1)
            {
                Context.Logger.LogWarning(
                    "DeepSeek request failed, retrying ({Attempt}/{MaxAttempts}) after {Delay}ms",
                    attempt, maxAttempts, retryDelay);
                await Task.Delay(retryDelay, cancellationToken);

                // 清掉上次尝试的半截译文，避免重试期间残留
                result.Text = string.Empty;
            }

            try
            {
                await ExecuteStreamingAsync(messages, text => result.Text = text, isValidation: false, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientFailure(ex, cancellationToken))
            {
                // 还有重试次数，继续循环
            }
        }
    }

    internal async Task ValidateApiAsync(CancellationToken cancellationToken = default)
    {
        // 与实际翻译走同一语言映射取值，避免验证与真实调用不一致
        var messages = BuildMessages(
            GetSourceLanguage(LangEnum.English)!,
            GetTargetLanguage(LangEnum.ChineseSimplified)!,
            "Hello world");
        await ExecuteStreamingAsync(messages, onTextUpdated: null, isValidation: true, cancellationToken);
    }

    private List<PromptItem> BuildMessages(string source, string target, string text)
    {
        var messages = (Prompts.FirstOrDefault(x => x.IsEnabled) ?? throw new Exception("请先完善Prompt配置"))
            .Clone()
            .Items
            .ToList();

        foreach (var item in messages)
        {
            item.Content = item.Content
                .Replace("$source", source)
                .Replace("$target", target)
                .Replace("$content", text);
        }

        return messages;
    }

    private async Task<string> ExecuteStreamingAsync(
        IReadOnlyCollection<PromptItem> messages,
        Action<string>? onTextUpdated,
        bool isValidation,
        CancellationToken cancellationToken)
    {
        // 构建最终URL（Path 留空时自动补全官方端点 /chat/completions，# 结尾强制使用原样地址）
        var url = UrlHelper.BuildFinalUrl(Settings.Url, "/chat/completions", UrlPathMatchRule.Strict);

        // 选择模型
        var model = Settings.Model.Trim();
        model = string.IsNullOrEmpty(model) ? "deepseek-v4-flash" : model;

        // 温度限定
        var temperature = Math.Clamp(Settings.Temperature, 0, 2);

        // 验证只需确认连通性与鉴权：关闭思考并压低 max_tokens，避免消耗
        var thinking = Settings.Thinking && !isValidation;

        // 构建请求体，参考 DeepSeek API 最新文档
        var content = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = temperature,
            ["max_tokens"] = isValidation ? Math.Min(Settings.MaxTokens, 128) : Settings.MaxTokens,
            ["top_p"] = Settings.TopP,
            ["stream"] = true,
            ["thinking"] = new { type = thinking ? "enabled" : "disabled" },
        };

        // 推理强度仅在思考模式下有意义，关闭时不发送
        if (thinking)
            content["reasoning_effort"] = Settings.ReasoningEffort;

        var option = new Options
        {
            Headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + Settings.ApiKey },
                { "Content-Type", "application/json" },
                { "Accept", "text/event-stream" }
            }
        };

        StringBuilder sb = new();
        var isThink = false;

        await Context.HttpService.StreamPostAsync(url, content, msg =>
        {
            var streamEvent = ParseStreamLine(msg);
            if (!string.IsNullOrWhiteSpace(streamEvent.ErrorMessage))
                throw new InvalidOperationException(streamEvent.ErrorMessage);

            var contentValue = streamEvent.TextDelta;
            if (string.IsNullOrEmpty(contentValue))
                return;

            // content 内嵌 <think></think> 的推理内容跳过（部分第三方服务商；reasoning_content 字段自然被忽略）
            if (contentValue.Trim() == "<think>")
            {
                isThink = true;
                return;
            }
            if (contentValue.Trim() == "</think>")
            {
                isThink = false;
                return;
            }
            if (isThink)
                return;

            // 跳过推理结束后的前导空白
            if (sb.Length == 0 && string.IsNullOrWhiteSpace(contentValue))
                return;

            sb.Append(contentValue);
            onTextUpdated?.Invoke(sb.ToString());
        }, option, cancellationToken: cancellationToken);

        if (sb.Length == 0)
            throw new InvalidOperationException(Context.GetTranslation("STranslate_Plugin_Translate_DeepSeek_NoTextOutput"));

        return sb.ToString();
    }

    private readonly record struct StreamEvent(string? TextDelta, string? ErrorMessage);

    private StreamEvent ParseStreamLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return default;

        // 仅去掉行首的 data: 前缀，避免误删 JSON 字符串值中出现的 "data:"
        var payload = line.StartsWith("data:", StringComparison.Ordinal)
            ? line["data:".Length..].Trim()
            : line.Trim();

        // 结束标记
        if (payload.Length == 0 || payload.Equals("[DONE]", StringComparison.Ordinal))
            return default;

        // 非 JSON 行（如 OpenRouter 心跳 ": OPENROUTER PROCESSING"）直接跳过，不走异常路径
        if (!payload.StartsWith('{'))
            return default;

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(payload);
        }
        catch
        {
            // 兜底畸形 JSON，记录以便排查第三方服务兼容问题
            Context.Logger.LogDebug("Skipped unparsable stream line: {Line}", payload);
            return default;
        }

        if (parsed is null)
            return default;

        // 流中错误事件（限流、配额不足等 OpenAI 兼容错误对象）
        if (parsed["error"]?["message"]?.ToString() is { Length: > 0 } errorMessage)
            return new StreamEvent(null, errorMessage);

        // choices 可能为空数组（部分服务商首包只带元数据）
        var choices = parsed["choices"] as JsonArray;
        var delta = choices is { Count: > 0 }
            ? choices[0]?["delta"]?["content"]?.ToString()
            : null;

        return string.IsNullOrEmpty(delta) ? default : new StreamEvent(delta, null);
    }

    // 宿主 HttpService 对非 2xx 手动抛 HttpRequestException 并填充 StatusCode；
    // 超时表现为 TaskCanceledException（不携带用户取消）；流中途断开为 IOException
    private static bool IsTransientFailure(Exception ex, CancellationToken userToken) => ex switch
    {
        OperationCanceledException => !userToken.IsCancellationRequested,
        HttpRequestException http => http.StatusCode is null
            or HttpStatusCode.TooManyRequests
            or >= HttpStatusCode.InternalServerError,
        IOException => true,
        _ => false,
    };
}
