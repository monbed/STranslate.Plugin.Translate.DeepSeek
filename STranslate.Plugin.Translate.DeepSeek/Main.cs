using Microsoft.Extensions.Logging;
using STranslate.Plugin.Translate.DeepSeek.View;
using STranslate.Plugin.Translate.DeepSeek.ViewModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
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
        LangEnum.Uyghur => "Uyghur",
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
        LangEnum.Uyghur => "Uyghur",
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
        // 构建最终URL（留空用官方地址，不以 /chat/completions 结尾时自动补上，# 结尾强制使用原样地址）
        var url = DeepSeekUrl.BuildChatUrl(Settings.Url);

        // 选择模型
        var model = Settings.Model.Trim();
        model = string.IsNullOrEmpty(model) ? "deepseek-v4-flash" : model;

        // 验证只需确认连通性与鉴权：关闭思考并压低 max_tokens，避免消耗
        var thinking = Settings.Thinking && !isValidation;

        // 构建请求体，参考 DeepSeek API 最新文档；
        // 参数顺序与设置界面一致：思考模式 → 推理强度 / 温度 → top_p → max_tokens
        var content = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = true,
            ["thinking"] = new { type = thinking ? "enabled" : "disabled" },
        };

        // 推理强度仅在思考模式下有意义；温度在思考模式下不生效（见官方文档），两者按模式二选一发送
        if (thinking)
            content["reasoning_effort"] = Settings.ReasoningEffort;
        else
            content["temperature"] = Math.Clamp(Settings.Temperature, 0, 2);

        content["top_p"] = Settings.TopP;

        // max_tokens 默认不发送，由服务端决定；验证时始终压到 128 以内，避免消耗
        if (isValidation)
            content["max_tokens"] = Math.Min(Settings.MaxTokens, 128);
        else if (Settings.SendMaxTokens)
            content["max_tokens"] = Math.Max(1, Settings.MaxTokens);

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
        string? finishReason = null;

        await Context.HttpService.StreamPostAsync(url, content, msg =>
        {
            var streamEvent = DeepSeekStream.ParseLine(
                msg,
                payload => Context.Logger.LogDebug("Skipped unparsable stream line: {Line}", payload));
            if (!string.IsNullOrWhiteSpace(streamEvent.ErrorMessage))
                throw new InvalidOperationException(streamEvent.ErrorMessage);

            // 结束原因随最后一个块下发，该块不含新增内容
            if (streamEvent.FinishReason is not null)
                finishReason = streamEvent.FinishReason;

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

        // 验证沿用原有判定：只看是否拿到文本，结束原因不参与判断
        if (isValidation)
        {
            if (sb.Length == 0)
                throw new InvalidOperationException(Context.GetTranslation("STranslate_Plugin_Translate_DeepSeek_NoTextOutput"));

            return sb.ToString();
        }

        // 推理资源不足属于服务端瞬时故障，交给外层按重试配置处理
        if (finishReason == DeepSeekStream.FinishInsufficientResource)
            throw new TransientStreamException(Context.GetTranslation(DeepSeekStream.GetIncompleteMessageKey(finishReason)!));

        // 输出未正常结束（长度截断 / 内容过滤 / 被中断）时的提示文本键
        var incompleteKey = DeepSeekStream.GetIncompleteMessageKey(finishReason);

        if (sb.Length == 0)
            throw new InvalidOperationException(
                Context.GetTranslation(incompleteKey ?? "STranslate_Plugin_Translate_DeepSeek_NoTextOutput"));

        // 有部分译文时保留已输出内容，并提示用户译文不完整
        if (incompleteKey is not null)
        {
            Context.Logger.LogWarning("DeepSeek response is incomplete, finish_reason: {FinishReason}", finishReason);
            Context.Snackbar.ShowWarning(Context.GetTranslation(incompleteKey), 5000);
        }

        return sb.ToString();
    }

    // 服务端主动中断且值得重试的流（如 finish_reason 为 insufficient_system_resource）
    private sealed class TransientStreamException(string message) : Exception(message);

    // 宿主 HttpService 对非 2xx 手动抛 HttpRequestException 并填充 StatusCode；
    // 超时表现为 TaskCanceledException（不携带用户取消）；流中途断开为 IOException
    private static bool IsTransientFailure(Exception ex, CancellationToken userToken) => ex switch
    {
        TransientStreamException => true,
        OperationCanceledException => !userToken.IsCancellationRequested,
        HttpRequestException http => http.StatusCode is null
            or HttpStatusCode.TooManyRequests
            or >= HttpStatusCode.InternalServerError,
        IOException => true,
        _ => false,
    };
}
