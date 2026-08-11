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
        MaxTokens = _settings.MaxTokens;
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
            case nameof(MaxTokens):
                // NumberBox 清空/非法输入时绑定不更新，这里只兜底下限
                _settings.MaxTokens = Math.Max(1, MaxTokens);
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
    [ObservableProperty] public partial int MaxTokens { get; set; }
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
            // 复用真实翻译管线（验证时自动关闭思考并压低 max_tokens）
            await Main.ValidateApiAsync();

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