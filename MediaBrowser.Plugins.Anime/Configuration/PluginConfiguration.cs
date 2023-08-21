using MediaBrowser.Model.Plugins;

namespace MediaBrowser.Plugins.Anime.Configuration
{
    public enum TitlePreferenceType
    {
        /// <summary>
        /// Use titles in the local metadata language.
        /// </summary>
        Localized,

        /// <summary>
        /// Use titles in Japanese.
        /// </summary>
        Japanese,

        /// <summary>
        /// Use titles in Japanese romaji.
        /// </summary>
        JapaneseRomaji,

        /// <summary>
        /// Use titles in German.
        /// </summary>
        German,
        
        /// <summary>
        /// Use titles in French.
        /// </summary>
        French,
    }

    public class PluginConfiguration
        : BasePluginConfiguration
    {
        public TitlePreferenceType TitlePreference { get; set; } = TitlePreferenceType.Localized;
        public bool TidyGenreList { get; set; } = true;
        public int AniDB_wait_time { get; set; } = 0;
    }
}
