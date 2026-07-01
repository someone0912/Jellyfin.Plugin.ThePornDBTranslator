using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ThePornDBTranslator
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool EnableTranslation { get; set; } = false;
        public string ApiUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
        public string ApiKey { get; set; } = "";
        public string ModelName { get; set; } = "gpt-3.5-turbo";
        public string PromptTemplate { get; set; } =
            "你是一个专业的成人影片元数据翻译专家，请将以下英文标题和简介翻译成中文，要求：\n" +
            "1. 保持专业术语准确（如：演员姓名、系列名称）\n" +
            "2. 标题要吸引人但不过分夸张\n" +
            "3. 简介要简洁明了，保留关键信息\n" +
            "4. 只返回翻译结果，不要添加任何解释\n\n" +
            "待翻译内容：\n{{text}}";
        public bool TranslateTitle { get; set; } = true;
        public bool TranslateOverview { get; set; } = true;
        public bool TranslateTagline { get; set; } = true;
        public bool TranslateActors { get; set; } = false; // ✅ 新增
        public int TimeoutSeconds { get; set; } = 30;
    }
}