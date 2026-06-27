using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ThePornDBTranslator
{
    public class LLMTranslationService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly string _apiUrl;
        private readonly string _apiKey;
        private readonly string _modelName;
        private readonly string _promptTemplate;
        private bool _disposed = false;

        public LLMTranslationService(
            string apiUrl,
            string apiKey,
            string modelName,
            string promptTemplate,
            ILogger logger,
            int timeoutSeconds = 30)
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            _logger = logger;
            _apiUrl = apiUrl;
            _apiKey = apiKey;
            _modelName = modelName;
            _promptTemplate = promptTemplate;
        }

        public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            try
            {
                string prompt = _promptTemplate.Replace("{{text}}", text);

                // ✅ 添加 thinking 参数，禁用思考模式
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
                    thinking = new { type = "disabled" }  // ✅ 禁用思考模式
                };

                string jsonBody = JsonSerializer.Serialize(requestBody);

                _logger.LogInformation(">>> 发送翻译请求: URL={ApiUrl}, Model={ModelName}, TextLength={TextLength}",
                    _apiUrl, _modelName, text.Length);

                var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request, cancellationToken);
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogInformation(">>> API 响应状态码: {StatusCode}, 是否成功: {IsSuccess}",
                    (int)response.StatusCode, response.IsSuccessStatusCode);

                // 完整响应体日志（用于调试）
                _logger.LogInformation(">>> 完整响应体: {ResponseBody}", responseBody);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(">>> API 请求失败: StatusCode={StatusCode}, Response={Response}",
                        (int)response.StatusCode, responseBody);
                    return text;
                }

                using var doc = JsonDocument.Parse(responseBody);

                // 提取 finish_reason
                string finishReason = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("finish_reason")
                    .GetString() ?? "unknown";

                _logger.LogInformation(">>> 完成原因: {FinishReason}", finishReason);

                if (finishReason == "length")
                {
                    _logger.LogWarning(">>> ⚠️ 响应因 max_tokens 限制被截断，翻译可能不完整");
                }

                // 提取 content
                string translated = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;

                // 记录翻译结果
                _logger.LogInformation(">>> 翻译结果: '{Translated}'", string.IsNullOrEmpty(translated) ? "[空]" : translated);

                if (string.IsNullOrEmpty(translated))
                {
                    _logger.LogWarning(">>> 翻译结果为空，请检查提示词或API响应");
                    return text;
                }

                _logger.LogInformation(">>> 原文长度: {OriginalLength}, 译文长度: {TranslatedLength}",
                    text.Length, translated.Length);

                return translated.Trim();
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogError(ex, "翻译请求超时 (超过 {TimeoutSeconds} 秒)", _httpClient.Timeout.TotalSeconds);
                return text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻译API调用异常: {Message}", ex.Message);
                return text;
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