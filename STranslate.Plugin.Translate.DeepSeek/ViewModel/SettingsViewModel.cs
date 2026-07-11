using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows;

namespace STranslate.Plugin.Translate.DeepSeek.ViewModel;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly Settings _settings;
    private bool _isUpdating = false;
    public Main Main { get; }

    public SettingsViewModel(IPluginContext context, Settings settings, Main main)
    {
        _context = context;
        _settings = settings;
        Main = main;

        Url = _settings.Url;
        ApiKey = _settings.ApiKey;
        Model = _settings.Model;
        Models = new ObservableCollection<string>(_settings.Models);
        Temperature = _settings.Temperature;
        Thinking = _settings.Thinking;
        ReasoningEffort = _settings.ReasoningEffort;

        PropertyChanged += OnPropertyChanged;
        Models.CollectionChanged += OnModelsCollectionChanged;
    }

    private void OnModelsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or
                       NotifyCollectionChangedAction.Remove or
                       NotifyCollectionChangedAction.Replace)
        {
            _settings.Models = [.. Models];
            _context.SaveSettingStorage<Settings>();
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ApiKey):
                _settings.ApiKey = ApiKey;
                break;
            case nameof(Url):
                _settings.Url = Url;
                break;
            case nameof(Model):
                _settings.Model = Model ?? string.Empty;
                break;
            case nameof(Temperature):
                // 舍入到一位小数，避免浮点精度问题
                _settings.Temperature = Math.Round(Temperature, 1);
                break;
            case nameof(Thinking):
                _settings.Thinking = Thinking;
                break;
            case nameof(ReasoningEffort):
                _settings.ReasoningEffort = ReasoningEffort;
                break;
            default:
                return;
        }
        _context.SaveSettingStorage<Settings>();
    }

    [ObservableProperty] public partial string ValidateResult { get; set; } = string.Empty;
    [ObservableProperty] public partial string BalanceResult { get; set; } = string.Empty;
    [ObservableProperty] public partial string Url { get; set; }
    [ObservableProperty] public partial string ApiKey { get; set; }
    [ObservableProperty] public partial string? Model { get; set; }
    [ObservableProperty] public partial ObservableCollection<string> Models { get; set; }
    [ObservableProperty] public partial double Temperature { get; set; }
    [ObservableProperty] public partial bool Thinking { get; set; }
    [ObservableProperty] public partial string ReasoningEffort { get; set; }

    [RelayCommand]
    private void AddModel(string model)
    {
        if (_isUpdating || string.IsNullOrWhiteSpace(model) || Models.Contains(model))
            return;

        using var _ = new UpdateGuard(this);

        Models.Add(model);
        Model = model;
    }

    [RelayCommand]
    private void DeleteModel(string model)
    {
        if (_isUpdating || !Models.Contains(model))
            return;

        using var _ = new UpdateGuard(this);

        if (Model == model)
            Model = Models.Count > 1 ? Models.First(m => m != model) : string.Empty;

        Models.Remove(model);
    }

    [RelayCommand]
    private void EditPrompt()
    {
        var dialog = _context.GetPromptEditWindow(Main.Prompts);

        if (dialog.ShowDialog() == true)
        {
            // 保存更新后的 Prompts
            _settings.Prompts = [.. Main.Prompts.Select(p => p.Clone())];
            _context.SaveSettingStorage<Settings>();

            // 更新选中项
            Main.SelectedPrompt = Main.Prompts.FirstOrDefault(p => p.IsEnabled);
        }
    }

    [RelayCommand]
    public async Task ValidateAsync()
    {
        try
        {
            // 构建最终URL（Path 留空时自动补全官方端点 /chat/completions，# 结尾强制使用原样地址）
            var url = UrlHelper.BuildFinalUrl(_settings.Url, "/chat/completions", UrlPathMatchRule.Strict);

            // 选择模型
            var model = _settings.Model.Trim();
            model = string.IsNullOrEmpty(model) ? "deepseek-v4-flash" : model;

            // 替换Prompt关键字
            var prompt = (Main.Prompts.FirstOrDefault(x => x.IsEnabled) ?? throw new Exception("请先完善Prompt配置"));
            var messages = prompt.Clone().Items;
            foreach (var item in messages)
            {
                item.Content = item.Content
                    .Replace("$source", "en-US")
                    .Replace("$target", "zh-CN")
                    .Replace("$content", "Hello world");
            }

            // 温度限定
            var temperature = Math.Clamp(_settings.Temperature, 0, 2);

            var content = new Dictionary<string, object>
            {
                ["model"] = model,
                ["messages"] = messages,
                ["temperature"] = temperature,
                ["max_tokens"] = _settings.MaxTokens,
                ["top_p"] = _settings.TopP,
                ["stream"] = _settings.Stream,
                ["thinking"] = new { type = _settings.Thinking ? "enabled" : "disabled" },
            };

            // 推理强度仅在思考模式下有意义，关闭时不发送
            if (_settings.Thinking)
                content["reasoning_effort"] = _settings.ReasoningEffort;

            var option = new Options
            {
                Headers = new Dictionary<string, string>
                {
                    { "Authorization", "Bearer " + _settings.ApiKey },
                    { "Content-Type", "application/json" },
                    { "Accept", "text/event-stream" }
                }
            };

            await _context.HttpService.StreamPostAsync(url, content, (x) => { }, option);

            ValidateResult = _context.GetTranslation("ValidationSuccess");
        }
        catch (Exception ex)
        {
            ValidateResult = _context.GetTranslation("ValidationFailure");
            _context.Logger.LogError(ex, _context.GetTranslation("ValidationFailure"));
        }
    }

    [RelayCommand]
    public async Task QueryBalanceAsync()
    {
        try
        {
            // 余额接口固定为 /user/balance，与聊天接口同源（兼容 # 结尾的强制地址写法）
            UriBuilder uriBuilder = new(_settings.Url.TrimEnd().TrimEnd('#')) { Path = "/user/balance", Query = string.Empty };

            var option = new Options
            {
                Headers = new Dictionary<string, string>
                {
                    { "Authorization", "Bearer " + _settings.ApiKey },
                    { "Accept", "application/json" }
                }
            };

            var resp = await _context.HttpService.GetAsync(uriBuilder.Uri.ToString(), option);

            // 响应结构见 https://api-docs.deepseek.com/zh-cn/api/get-user-balance
            var infos = JsonNode.Parse(resp)?["balance_infos"]?.AsArray()
                ?? throw new Exception($"Unexpected response: {resp}");

            var text = string.Join("  ", infos.Select(i => $"{i?["total_balance"]} {i?["currency"]}"));
            BalanceResult = string.IsNullOrEmpty(text) ? GetResourceString("STranslate_Plugin_Translate_DeepSeek_Balance_Failed", "查询失败") : text;
        }
        catch (Exception ex)
        {
            BalanceResult = GetResourceString("STranslate_Plugin_Translate_DeepSeek_Balance_Failed", "查询失败");
            _context.Logger.LogError(ex, "Query DeepSeek balance failed");
        }
    }

    private static string GetResourceString(string key, string fallback) =>
        Application.Current?.TryFindResource(key) as string ?? fallback;

    public void Dispose()
    {
        PropertyChanged -= OnPropertyChanged;
        Models.CollectionChanged -= OnModelsCollectionChanged;
    }

    // 辅助类和记录
    private readonly struct UpdateGuard : IDisposable
    {
        private readonly SettingsViewModel _viewModel;

        public UpdateGuard(SettingsViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel._isUpdating = true;
        }

        public void Dispose() => _viewModel._isUpdating = false;
    }
}