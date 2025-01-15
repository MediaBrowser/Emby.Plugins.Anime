using MediaBrowser.Model.Plugins;

namespace MediaBrowser.Plugins.Anime.Configuration
{
    public class PluginConfiguration
        : BasePluginConfiguration
    {
        public bool TidyGenreList { get; set; } = true;
        public int AniDB_wait_time { get; set; } = 0;

        public TitleLanguageOption PreferredTitleLanguage { get; set; } = TitleLanguageOption.UseLibrarySetting;
    }

    public enum TitleLanguageOption
    {
        UseLibrarySetting,
        Romaji,
    }
}