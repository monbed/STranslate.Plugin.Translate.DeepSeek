namespace STranslate.Plugin.Translate.DeepSeek;

/// <summary>
/// 由“接口”设置推导实际请求地址。
/// 规则：留空使用官方地址；不以 /chat/completions 结尾时自动补上；以 # 结尾则原样使用。
/// </summary>
internal static class DeepSeekUrl
{
    internal const string DefaultUrl = "https://api.deepseek.com";

    private const string ChatPath = "/chat/completions";
    private const string ModelsPath = "/models";
    private const string BalancePath = "/user/balance";

    /// <summary>
    /// 聊天接口地址
    /// </summary>
    internal static string BuildChatUrl(string? url)
    {
        var input = url?.Trim() ?? string.Empty;

        // 以 # 结尾：强制使用填写的地址，不做补全
        var forced = input.EndsWith('#');
        if (forced)
            input = input.TrimEnd('#').TrimEnd();

        // 留空（或只填了 #）时回退到官方地址，并照常补全
        if (input.Length == 0)
        {
            input = DefaultUrl;
            forced = false;
        }

        try
        {
            var builder = new UriBuilder(input);

            if (!forced)
            {
                var path = builder.Path.TrimEnd('/');
                if (!path.EndsWith(ChatPath, StringComparison.OrdinalIgnoreCase))
                    builder.Path = path + ChatPath;
            }

            return builder.Uri.ToString();
        }
        catch
        {
            // 无法解析的地址原样返回，由请求阶段报错
            return input;
        }
    }

    /// <summary>
    /// 模型列表地址，与聊天接口同级：.../chat/completions -> .../models。
    /// 用 # 强制成其他路径时无法推断前缀，退回到站点根目录的 /models
    /// </summary>
    internal static string BuildModelsUrl(string? url)
    {
        var builder = new UriBuilder(BuildChatUrl(url)) { Fragment = string.Empty };

        var path = builder.Path.TrimEnd('/');
        builder.Path = path.EndsWith(ChatPath, StringComparison.OrdinalIgnoreCase)
            ? path[..^ChatPath.Length] + ModelsPath
            : ModelsPath;

        return builder.Uri.ToString();
    }

    /// <summary>
    /// 余额接口地址，固定为站点根目录的 /user/balance（官方接口不带路径前缀）
    /// </summary>
    internal static string BuildBalanceUrl(string? url)
    {
        var builder = new UriBuilder(BuildChatUrl(url))
        {
            Path = BalancePath,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return builder.Uri.ToString();
    }
}
