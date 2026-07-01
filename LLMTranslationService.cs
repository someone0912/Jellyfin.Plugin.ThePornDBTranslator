using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ThePornDBTranslator
{
    public class TranslationContext
    {
        public string Title { get; set; } = string.Empty;
        public string Overview { get; set; } = string.Empty;
        public string Tagline { get; set; } = string.Empty;
        public List<string> ActorNames { get; set; } = new List<string>();
        public string Studio { get; set; } = string.Empty;
    }

    public class LLMTranslationService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly string _apiUrl;
        private readonly string _apiKey;
        private readonly string _modelName;
        private readonly PluginConfiguration _config;
        private bool _disposed = false;
          
        public LLMTranslationService(
            string apiUrl,
            string apiKey,
            string modelName,
            ILogger logger,
            int timeoutSeconds = 30)
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            _logger = logger;
            _apiUrl = apiUrl;
            _apiKey = apiKey;
            _modelName = modelName;
            _config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        }

        public async Task<string> TranslateTitleAsync(TranslationContext context, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(context.Title))
                return context.Title;

            string prompt = BuildPromptFromConfig(context.Title, "标题", context);
            return await TranslateInternalAsync(prompt, context.Title, cancellationToken);
        }

        public async Task<string> TranslateOverviewAsync(TranslationContext context, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(context.Overview))
                return context.Overview;

            string prompt = BuildPromptFromConfig(context.Overview, "简介", context);
            return await TranslateInternalAsync(prompt, context.Overview, cancellationToken);
        }

        public async Task<string> TranslateTaglineAsync(TranslationContext context, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(context.Tagline))
                return context.Tagline;

            string prompt = BuildPromptFromConfig(context.Tagline, "标语", context);
            return await TranslateInternalAsync(prompt, context.Tagline, cancellationToken);
        }

        /// <summary>
        /// 从配置的 PromptTemplate 构建提示词，支持以下占位符：
        /// {{text}} - 待翻译的原文
        /// {{field}} - 字段类型（标题/简介/标语）
        /// {{actors}} - 演员姓名列表
        /// {{studio}} - 制作公司/品牌
        /// {{actors_instruction}} - 根据 TranslateActors 生成的完整指令
        /// {{studio_instruction}} - 品牌保护指令
        /// </summary>
        private string BuildPromptFromConfig(string text, string fieldType, TranslationContext context)
        {
            // 1. 构建演员指令（强化版）
            string actorsDisplay = context.ActorNames != null && context.ActorNames.Count > 0
                ? string.Join(", ", context.ActorNames)
                : "无";

            string actorsInstruction;
            if (_config.TranslateActors)
            {
                // 翻译演员名称
                actorsInstruction = $"【演员名称（需要翻译成中文）】\n{actorsDisplay}\n请将这些演员姓名翻译成中文，使用成人娱乐行业通用的中文译名。";
            }
            else
            {
                // 不翻译演员名称：强化保护
                actorsInstruction = $@"【⚠️ 最高优先级：绝对禁止翻译以下演员姓名】
演员姓名：{actorsDisplay}
规则：这些演员名必须 100% 原样保留在翻译结果中，绝对不能翻译成中文。";
            }

            // 2. 构建品牌保护指令（强化版）
            string studioDisplay = string.IsNullOrWhiteSpace(context.Studio) ? "无" : context.Studio;
            string studioInstruction;
            if (studioDisplay != "无")
            {
                studioInstruction = $@"【⚠️ 最高优先级：绝对禁止翻译以下品牌/工作室名称】
品牌名称：{studioDisplay}
规则：这些品牌名必须 100% 原样保留在翻译结果中，绝对不能翻译成中文。";
            }
            else
            {
                studioInstruction = "【品牌/工作室】无";
            }

            // 3. 获取配置模板
            string template = string.IsNullOrWhiteSpace(_config.PromptTemplate)
                ? GetDefaultPromptTemplate()
                : _config.PromptTemplate;

            // 4. 替换所有占位符
            string prompt = template
                .Replace("{{text}}", text)
                .Replace("{{field}}", fieldType)
                .Replace("{{actors}}", actorsDisplay)
                .Replace("{{studio}}", studioDisplay)
                .Replace("{{actors_instruction}}", actorsInstruction)
                .Replace("{{studio_instruction}}", studioInstruction);

            // 5. 额外保护：在末尾再次强调（针对不翻译的情况）
            if (actorsDisplay != "无" && !_config.TranslateActors)
            {
                prompt += $"\n\n【再次强调】原文中包含演员名 \"{actorsDisplay}\"，这些词绝对不能翻译，必须原样保留。";
            }
            if (studioDisplay != "无")
            {
                prompt += $"\n【再次强调】原文中包含品牌名 \"{studioDisplay}\"，这些词绝对不能翻译，必须原样保留。";
            }

            return prompt;
        }

        /// <summary>
        /// 默认提示词模板（当配置为空时使用）
        /// </summary>
        private string GetDefaultPromptTemplate()
        {
            return @"你是一位专业的成人影片内容本地化专家。请将以下英文{{field}}翻译成中文。

【最高优先级规则：必须原样保留以下内容】
{{actors_instruction}}
{{studio_instruction}}

【待翻译的{{field}}】
{{text}}

【翻译要求】
1. 只翻译剧情和场景描述，用词要能激发性欲和好奇心
2. 【最高优先级】演员姓名和品牌/工作室名称必须原样保留，绝对不能翻译
3. 使用成人娱乐行业通用的中文术语
4. 保持吸引力和故事感，但不过分低俗夸张
5. 只返回翻译结果，不要添加任何解释";
        }

        private async Task<string> TranslateInternalAsync(string prompt, string originalText, CancellationToken cancellationToken)
        {
            try
            {
                var requestBody = new
                {
                    model = _modelName,
                    messages = new[]
                    {
                        new { role = "system", content = "你是专业的成人影片元数据翻译专家" },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.3,
                    max_tokens = 2000,
                    thinking = new { type = "disabled" }
                };

                string jsonBody = JsonSerializer.Serialize(requestBody);

                _logger.LogInformation(">>> 发送翻译请求: URL={ApiUrl}, Model={ModelName}, TextLength={TextLength}",
                    _apiUrl, _modelName, originalText.Length);

                var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request, cancellationToken);
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogInformation(">>> API 响应状态码: {StatusCode}, 是否成功: {IsSuccess}",
                    (int)response.StatusCode, response.IsSuccessStatusCode);

                _logger.LogInformation(">>> 完整响应体: {ResponseBody}", responseBody);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(">>> API 请求失败: StatusCode={StatusCode}, Response={Response}",
                        (int)response.StatusCode, responseBody);
                    return originalText;
                }

                using var doc = JsonDocument.Parse(responseBody);

                string finishReason = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("finish_reason")
                    .GetString() ?? "unknown";

                _logger.LogInformation(">>> 完成原因: {FinishReason}", finishReason);

                if (finishReason == "length")
                {
                    _logger.LogWarning(">>> ⚠️ 响应因 max_tokens 限制被截断，翻译可能不完整");
                }

                string translated = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;

                _logger.LogInformation(">>> 翻译结果: '{Translated}'", string.IsNullOrEmpty(translated) ? "[空]" : translated);

                if (string.IsNullOrEmpty(translated))
                {
                    _logger.LogWarning(">>> 翻译结果为空，请检查提示词或API响应");
                    return originalText;
                }

                _logger.LogInformation(">>> 原文长度: {OriginalLength}, 译文长度: {TranslatedLength}",
                    originalText.Length, translated.Length);

                return translated.Trim();
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogError(ex, "翻译请求超时 (超过 {TimeoutSeconds} 秒)", _httpClient.Timeout.TotalSeconds);
                return originalText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻译API调用异常: {Message}", ex.Message);
                return originalText;
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
                _httpClient?.Dispose();
            }
            _disposed = true;
        }
    }
}