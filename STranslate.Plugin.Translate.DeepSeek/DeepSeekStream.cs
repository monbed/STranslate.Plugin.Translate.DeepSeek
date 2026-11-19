using System.Text.Json.Nodes;

namespace STranslate.Plugin.Translate.DeepSeek;

internal readonly record struct StreamEvent(string? TextDelta, string? ErrorMessage, string? FinishReason);

/// <summary>
/// 流式响应（SSE）解析
/// </summary>
internal static class DeepSeekStream
{
    // finish_reason 取值见 https://api-docs.deepseek.com/zh-cn/api/create-chat-completion
    internal const string FinishLength = "length";
    internal const string FinishContentFilter = "content_filter";
    internal const string FinishInsufficientResource = "insufficient_system_resource";
    internal const string FinishAborted = "aborted";

    /// <summary>
    /// 解析一行 SSE 数据；无可用信息的行返回 default
    /// </summary>
    /// <param name="line">原始行</param>
    /// <param name="onUnparsable">遇到畸形 JSON 时回调，用于记录日志</param>
    internal static StreamEvent ParseLine(string? line, Action<string>? onUnparsable = null)
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
            // 兜底畸形 JSON，交给调用方记录以便排查第三方服务兼容问题
            onUnparsable?.Invoke(payload);
            return default;
        }

        if (parsed is null)
            return default;

        // 流中错误事件（限流、配额不足等 OpenAI 兼容错误对象）
        if (parsed["error"]?["message"]?.ToString() is { Length: > 0 } errorMessage)
            return new StreamEvent(null, errorMessage, null);

        // choices 可能为空数组（部分服务商首包只带元数据）
        var choice = parsed["choices"] is JsonArray { Count: > 0 } choices
            ? choices[0] as JsonObject
            : null;

        var delta = choice?["delta"]?["content"]?.ToString();

        // 最后一个块不含新增内容，只带非 null 的 finish_reason
        var finishReason = choice?["finish_reason"]?.ToString();

        if (string.IsNullOrEmpty(delta) && string.IsNullOrEmpty(finishReason))
            return default;

        return new StreamEvent(
            string.IsNullOrEmpty(delta) ? null : delta,
            null,
            string.IsNullOrEmpty(finishReason) ? null : finishReason);
    }

    /// <summary>
    /// 输出未正常结束时对应的提示文本资源键；正常结束（stop 等）或未知取值返回 null
    /// </summary>
    internal static string? GetIncompleteMessageKey(string? finishReason) => finishReason switch
    {
        FinishLength => "STranslate_Plugin_Translate_DeepSeek_FinishReason_Length",
        FinishContentFilter => "STranslate_Plugin_Translate_DeepSeek_FinishReason_ContentFilter",
        FinishInsufficientResource => "STranslate_Plugin_Translate_DeepSeek_FinishReason_InsufficientResource",
        FinishAborted => "STranslate_Plugin_Translate_DeepSeek_FinishReason_Aborted",
        _ => null,
    };
}
