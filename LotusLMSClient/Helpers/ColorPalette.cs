using System.Drawing;

namespace LotusLMSClient.Helpers
{
    public static class ColorPalette
    {
        public static readonly Color Primary = ColorTranslator.FromHtml("#1890FF");
        public static readonly Color PrimaryDark = ColorTranslator.FromHtml("#096DD9");
        public static readonly Color PrimaryLight = ColorTranslator.FromHtml("#40A9FF");

        public static readonly Color SidebarBg = ColorTranslator.FromHtml("#1B2838");
        public static readonly Color SidebarHover = ColorTranslator.FromHtml("#2C3E50");
        public static readonly Color SidebarActive = ColorTranslator.FromHtml("#1890FF");
        public static readonly Color SidebarText = ColorTranslator.FromHtml("#ADB5BD");

        public static readonly Color BgGradientStart = ColorTranslator.FromHtml("#4A90D9");
        public static readonly Color BgGradientEnd = ColorTranslator.FromHtml("#7EC8E3");
        public static readonly Color ContentBg = ColorTranslator.FromHtml("#F0F2F5");
        public static readonly Color CardBg = Color.White;

        public static readonly Color TextPrimary = ColorTranslator.FromHtml("#262626");
        public static readonly Color TextSecondary = ColorTranslator.FromHtml("#8C8C8C");
        public static readonly Color TitleGold = ColorTranslator.FromHtml("#D4A853");

        public static readonly Color Success = ColorTranslator.FromHtml("#52C41A");
        public static readonly Color Warning = ColorTranslator.FromHtml("#FAAD14");
        public static readonly Color Error = ColorTranslator.FromHtml("#FF4D4F");
    }
}
