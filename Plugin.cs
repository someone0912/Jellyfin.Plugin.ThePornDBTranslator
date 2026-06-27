using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ThePornDBTranslator
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static Plugin? Instance { get; private set; }
        private readonly ThePornDBTranslationService? _translationService;

        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILogger<Plugin> logger,
            IServiceProvider serviceProvider)  // ✅ 注入服务提供者
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            Logger = logger;
            Logger.LogInformation("ThePornDB翻译器插件已加载");

            try
            {
                // ✅ 从服务容器中获取 ILibraryManager
                var libraryManager = serviceProvider.GetRequiredService<ILibraryManager>();

                // ✅ 从服务容器中获取 ILoggerFactory
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

                // ✅ 使用 ILoggerFactory 创建 ThePornDBTranslationService 的日志实例
                var translationLogger = loggerFactory.CreateLogger<ThePornDBTranslationService>();

                // ✅ 创建服务实例
                _translationService = new ThePornDBTranslationService(
                    translationLogger,
                    libraryManager
                );

                Logger.LogInformation("ThePornDBTranslationService 已成功创建并初始化");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "创建 ThePornDBTranslationService 失败");
            }
        }

        public ILogger<Plugin> Logger { get; }

        public override string Name => "ThePornDBTranslator";
        public override string Description => "专门翻译ThePornDB刮削的元数据（独立模块）";
        public override Guid Id => Guid.Parse("8BD0C0E5-433F-4047-B46B-E60DF1EA7C97");

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "ThePornDBTranslator",
                    EmbeddedResourcePath = "Jellyfin.Plugin.ThePornDBTranslator.Configuration.config.html"
                }
            };
        }
    }
}