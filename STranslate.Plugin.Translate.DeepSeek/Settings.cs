namespace STranslate.Plugin.Translate.DeepSeek;

public class Settings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Url { get; set; } = "https://api.deepseek.com/";
    public string Model { get; set; } = "deepseek-v4-flash";
    public List<string> Models { get; set; } =
    [
        "deepseek-v4-flash",
        "deepseek-v4-pro",
    ];
    public int MaxTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 1;
    public bool Stream { get; set; } = true;
    public double FrequencyPenalty { get; set; } = 0;
    public double PresencePenalty { get; set; } = 0;
    /// <summary>
    /// 思考模式开关，默认开启
    /// </summary>
    public bool Thinking { get; set; } = true;
    /// <summary>
    /// 推理深度控制：high / max，默认 high
    /// </summary>
    public string ReasoningEffort { get; set; } = "high";
    public int? MaxRetries { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 1000;

    public List<Prompt> Prompts { get; set; } =
    [
        new("翻译",
        [
            new PromptItem("system", "You are a professional, authentic translation engine. You only return the translated text, without any explanations."),
            new PromptItem("user", "Please translate  into $target (avoid explaining the original text):\r\n\r\n$content"),
        ], true),
        new("润色",
        [
            new PromptItem("system", "You are a professional, authentic text polishing engine. You only return the polished text, without any explanations."),
            new PromptItem("user", "Please polish the following text in $source (avoid explaining the original text):\r\n\r\n$content"),
        ]),
        new("总结",
        [
            new PromptItem("system", "You are a professional, authentic text summarization engine. You only return the summarized text, without any explanations."),
            new PromptItem("user", "Please summarize the following text in $source (avoid explaining the original text):\r\n\r\n$content"),
        ]),
    ];
}