using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Plugins.Anime.Providers.AniDB
{
    public class AniDbSeriesExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item)
        {
            return item is Series;
        }

        public string Name
        {
            get { return "AniDB"; }
        }

        public string Key
        {
            get { return ProviderNames.AniDb; }
        }

        public string UrlFormatString
        {
            get { return "http://anidb.net/perl-bin/animedb.pl?show=anime&aid={0}"; }
        }
    }
    public class AniDbEpisodeExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item)
        {
            return item is Episode;
        }

        public string Name
        {
            get { return "AniDB"; }
        }

        public string Key
        {
            get { return ProviderNames.AniDb; }
        }

        public string UrlFormatString
        {
            get { return null; }
        }
    }
    public class AniDbPersonExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item)
        {
            return item is Person;
        }

        public string Name
        {
            get { return "AniDB"; }
        }

        public string Key
        {
            get { return ProviderNames.AniDb; }
        }

        public string UrlFormatString
        {
            get { return null; }
        }
    }
}