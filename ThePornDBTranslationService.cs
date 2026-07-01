using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ThePornDBTranslator
{
    public class ThePornDBTranslationService : IDisposable
    {
        private readonly ILogger<ThePornDBTranslationService> _logger;
        private readonly LLMTranslationService? _translator;
        private readonly PluginConfiguration _config;
        private readonly ILibraryManager _libraryManager;
        private bool _disposed = false;
        private const string TranslatedTag = "Translated";

        public ThePornDBTranslationService(
            ILogger<ThePornDBTranslationService> logger,
            ILibraryManager libraryManager)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

            _logger.LogInformation(">>> ThePornDBTranslationService 构造函数被执行，EnableTranslation: {Enable}", _config.EnableTranslation);

            if (_config.EnableTranslation)
            {
                try
                {
                    _translator = new LLMTranslationService(
                        _config.ApiUrl,
                        _config.ApiKey,
                        _config.ModelName,
                        logger,
                        _config.TimeoutSeconds
                    );

                    _libraryManager.ItemAdded += OnItemAdded;
                    _libraryManager.ItemUpdated += OnItemUpdated;

                    _logger.LogInformation("ThePornDB翻译器已启用，已订阅 ItemAdded 和 ItemUpdated 事件");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "初始化翻译服务失败");
                }
            }
            else
            {
                _logger.LogInformation("ThePornDB翻译器未启用（配置中 EnableTranslation 为 false）");
            }
        }

        private async void OnItemAdded(object? sender, ItemChangeEventArgs e)
        {
            try
            {
                _logger.LogInformation(">>> 捕获到 ItemAdded 事件: {Name}", e.Item?.Name ?? "null");
                await ProcessItem(e.Item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnItemAdded 处理异常");
            }
        }

        private async void OnItemUpdated(object? sender, ItemChangeEventArgs e)
        {
            try
            {
                _logger.LogInformation(">>> 捕获到 ItemUpdated 事件: {Name}", e.Item?.Name ?? "null");
                await ProcessItem(e.Item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnItemUpdated 处理异常");
            }
        }

        private async Task ProcessItem(BaseItem? item)
        {
            if (!_config.EnableTranslation || _translator == null)
            {
                _logger.LogInformation(">>> ❌ 翻译未启用或服务未初始化，跳过");
                return;
            }

            if (item is not Movie movie)
            {
                _logger.LogInformation(">>> ❌ 不是 Movie 类型，跳过");
                return;
            }

            if (movie.Tags != null && movie.Tags.Contains(TranslatedTag))
            {
                _logger.LogInformation(">>> ⏭️ 影片已包含 '{Tag}' 标签，跳过翻译", TranslatedTag);
                return;
            }

            bool isFromThePornDB = false;
            if (movie.ProviderIds != null)
            {
                isFromThePornDB = movie.ProviderIds.Any(kvp =>
                    kvp.Key?.Contains("ThePornDB", StringComparison.OrdinalIgnoreCase) == true);
                _logger.LogInformation(">>> ProviderIds: {ProviderIds}",
                    string.Join(", ", movie.ProviderIds.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            }

            if (!isFromThePornDB)
            {
                _logger.LogInformation(">>> ❌ 不是 ThePornDB 来源，跳过翻译");
                return;
            }

            _logger.LogInformation($">>> ✅ 开始翻译: {movie.Name}");

            // ========== 构建翻译上下文 ==========
            var people = _libraryManager.GetPeople(movie);
            var actorNames = people?
                .Where(p => p.Type == PersonKind.Actor)
                .Select(p => p.Name)
                .Distinct()
                .ToList() ?? new List<string>();

            var context = new TranslationContext
            {
                Title = movie.Name ?? string.Empty,
                Overview = movie.Overview ?? string.Empty,
                Tagline = movie.Tagline ?? string.Empty,
                Studio = movie.Studios != null && movie.Studios.Length > 0
                    ? string.Join(", ", movie.Studios)
                    : string.Empty,
                ActorNames = actorNames
            };

            _logger.LogInformation(">>> 提取到 {ActorCount} 位演员，工作室: {Studio}",
                context.ActorNames.Count, string.IsNullOrEmpty(context.Studio) ? "无" : context.Studio);

            // ========== 重试循环 ==========
            int maxRetries = 3;
            int retryDelaySeconds = 30;
            bool translationSuccess = false;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogInformation(">>> 🔄 翻译尝试 {Attempt}/{MaxRetries}", attempt, maxRetries);
                    bool needUpdate = false;

                    // 翻译标题
                    if (_config.TranslateTitle && !string.IsNullOrEmpty(context.Title))
                    {
                        _logger.LogInformation(">>> 📝 翻译前标题: '{Title}'", context.Title);
                        var translated = await _translator.TranslateTitleAsync(context);
                        _logger.LogInformation(">>> 📝 翻译后标题: '{Translated}'", translated);

                        if (string.IsNullOrEmpty(translated))
                            throw new Exception("翻译返回空结果（标题）");

                        if (translated != context.Title)
                        {
                            movie.Name = translated;
                            needUpdate = true;
                            _logger.LogInformation(">>> ✅ 标题已更新: '{Translated}'", translated);
                        }
                        else
                        {
                            _logger.LogInformation(">>> ⚠️ 标题翻译结果与原文相同");
                        }
                    }

                    // 翻译简介
                    if (_config.TranslateOverview && !string.IsNullOrEmpty(context.Overview))
                    {
                        _logger.LogInformation(">>> 📝 翻译前简介: '{Overview}'", context.Overview);
                        var translated = await _translator.TranslateOverviewAsync(context);
                        _logger.LogInformation(">>> 📝 翻译后简介: '{Translated}'", translated);

                        if (string.IsNullOrEmpty(translated))
                            throw new Exception("翻译返回空结果（简介）");

                        if (translated != context.Overview)
                        {
                            movie.Overview = translated;
                            needUpdate = true;
                            _logger.LogInformation(">>> ✅ 简介已更新: '{Translated}'", translated);
                        }
                        else
                        {
                            _logger.LogInformation(">>> ⚠️ 简介翻译结果与原文相同");
                        }
                    }

                    // 翻译标语
                    if (_config.TranslateTagline && !string.IsNullOrEmpty(context.Tagline))
                    {
                        _logger.LogInformation(">>> 📝 翻译前标语: '{Tagline}'", context.Tagline);
                        var translated = await _translator.TranslateTaglineAsync(context);
                        _logger.LogInformation(">>> 📝 翻译后标语: '{Translated}'", translated);

                        if (string.IsNullOrEmpty(translated))
                            throw new Exception("翻译返回空结果（标语）");

                        if (translated != context.Tagline)
                        {
                            movie.Tagline = translated;
                            needUpdate = true;
                            _logger.LogInformation(">>> ✅ 标语已更新: '{Translated}'", translated);
                        }
                        else
                        {
                            _logger.LogInformation(">>> ⚠️ 标语翻译结果与原文相同");
                        }
                    }

                    // 保存更新
                    if (needUpdate)
                    {
                        var tagsList = new List<string>();
                        if (movie.Tags != null) tagsList.AddRange(movie.Tags);

                        if (!tagsList.Contains(TranslatedTag))
                        {
                            tagsList.Add(TranslatedTag);
                            movie.Tags = tagsList.ToArray();
                            _logger.LogInformation(">>> ✅ 已添加 '{Tag}' 标签", TranslatedTag);
                        }

                        await movie.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None);
                        _logger.LogInformation($"✅ 翻译完成并已保存: {movie.Name}");
                        translationSuccess = true;
                        break;
                    }
                    else
                    {
                        _logger.LogInformation(">>> ⏭️ 没有需要更新的字段");
                        translationSuccess = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ">>> ❌ 翻译尝试 {Attempt}/{MaxRetries} 失败", attempt, maxRetries);

                    if (attempt < maxRetries)
                    {
                        _logger.LogInformation(">>> ⏳ 等待 {Delay} 秒后重试...", retryDelaySeconds);
                        await Task.Delay(retryDelaySeconds * 1000);
                    }
                    else
                    {
                        _logger.LogWarning(">>> ❌ 翻译失败 {Attempt} 次，已放弃", maxRetries);
                    }
                }
            }

            if (!translationSuccess)
            {
                _logger.LogWarning(">>> ❌ 翻译最终失败，请稍后手动刷新元数据重试");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                if (_libraryManager != null)
                {
                    _libraryManager.ItemAdded -= OnItemAdded;
                    _libraryManager.ItemUpdated -= OnItemUpdated;
                }
                _translator?.Dispose();
            }
            _disposed = true;
        }
    }
}