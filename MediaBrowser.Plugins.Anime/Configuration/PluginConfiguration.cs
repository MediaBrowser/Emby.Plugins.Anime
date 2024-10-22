using MediaBrowser.Model.Plugins;

namespace MediaBrowser.Plugins.Anime.Configuration
{
    public class PluginConfiguration
        : BasePluginConfiguration
    {
        public bool TidyGenreList { get; set; } = true;
        public int AniDB_wait_time { get; set; } = 0;
    }
}