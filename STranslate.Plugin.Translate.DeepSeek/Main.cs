using STranslate.Plugin.Translate.DeepSeek.View;
using STranslate.Plugin.Translate.DeepSeek.ViewModel;
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

        // 构建最终URL（Path 留空时自动补全官方端点 /chat/completions，# 结尾强制使用原样地址）
        var url = UrlHelper.BuildFinalUrl(Settings.Url, "/chat/completions", UrlPathMatchRule.Strict);

        // 选择模型
        var model = Settings.Model.Trim();
        model = string.IsNullOrEmpty(model) ? "deepseek-v4-flash" : model;

        // 替换Prompt关键字
        var messages = (Prompts.FirstOrDefault(x => x.IsEnabled) ?? throw new Exception("请先完善Prompt配置"))
            .Clone()
            .Items;
        messages.ToList()
            .ForEach(item =>
                item.Content = item.Content
                .Replace("$source", sourceStr)
                .Replace("$target", targetStr)
                .Replace("$content", request.Text)
                );

        // 温度限定
        var temperature = Math.Clamp(Settings.Temperature, 0, 2);

        // 构建请求体，参考 DeepSeek API 最新文档
        var content = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = temperature,
            ["max_tokens"] = Settings.MaxTokens,
            ["top_p"] = Settings.TopP,
            ["stream"] = Settings.Stream,
            ["thinking"] = new { type = Settings.Thinking ? "enabled" : "disabled" },
        };

        // 推理强度仅在思考模式下有意义，关闭时不发送
        if (Settings.Thinking)
            content["reasoning_effort"] = Settings.ReasoningEffort;

        // 请求头，使用标准字段名并兼容流式返回
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
            if (string.IsNullOrEmpty(msg?.Trim()))
                return;

            var preprocessString = msg.Replace("data:", "").Trim();

            // 结束标记
            if (preprocessString.Equals("[DONE]"))
                return;

            try
            {
                // 解析JSON数据
                var parsedData = JsonNode.Parse(preprocessString);

                if (parsedData is null)
                    return;

                // 提取content的值
                var contentValue = parsedData["choices"]?[0]?["delta"]?["content"]?.ToString();

                if (string.IsNullOrEmpty(contentValue))
                    return;

                /***********************************************************************
                 * 推理模型思考内容
                 * 1. content字段内：部分服务商用 <think></think> 标签包裹（推理后带有换行）
                 * 2. reasoning_content字段内：DeepSeek、硅基流动（推理后带有换行）、第三方服务商
                 ************************************************************************/

                #region 针对content内容中含有推理内容的优化

                if (contentValue.Trim() == "<think>")
                    isThink = true;
                if (contentValue.Trim() == "</think>")
                {
                    isThink = false;
                    // 跳过当前内容
                    return;
                }

                if (isThink)
                    return;

                #endregion

                #region 针对推理过后带有换行的情况进行优化

                // 优化推理模型思考结束后的\n\n符号
                if (string.IsNullOrWhiteSpace(sb.ToString()) && string.IsNullOrWhiteSpace(contentValue))
                    return;

                sb.Append(contentValue);

                #endregion

                result.Text = sb.ToString();
            }
            catch
            {
                // Ignore
                // * 适配 OpenRouter 等第三方服务流数据中包含与 DeepSeek 官方 API 不同的数据
                // * 如 ": OPENROUTER PROCESSING"，非 JSON 行解析失败时在此兜底忽略
            }
        }, option, cancellationToken: cancellationToken);
    }
}