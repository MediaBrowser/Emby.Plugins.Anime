using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Plugins.Anime.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Plugins.Anime.Providers.AniDB.Metadata
{
    public class AniDbSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        private const string SeriesDataFile = "series.xml";
        private const string SeriesQueryUrl = "http://api.anidb.net:9001/httpapi?request=anime&client={0}&clientver=1&protover=1&aid={1}";
        private const string ClientName = "mediabrowser";

        // AniDB has very low request rate limits, a minimum of 2 seconds between requests, and an average of 4 seconds between requests
        //public static readonly SemaphoreSlim ResourcePool = new SemaphoreSlim(1, 1);

        public static readonly RateLimiter RequestLimiter = new RateLimiter(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
        private static readonly int[] IgnoredCategoryIds = { 6, 22, 23, 60, 128, 129, 185, 216, 242, 255, 268, 269, 289 };
        private static readonly Regex AniDbUrlRegex = new Regex(@"http://anidb.net/\w+ \[(?<name>[^\]]*)\]");
        private readonly IApplicationPaths _appPaths;
        private readonly IHttpClient _httpClient;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;

        private readonly Dictionary<string, PersonType> _typeMappings = new Dictionary<string, PersonType>(StringComparer.OrdinalIgnoreCase)
        {
            {"Direction", PersonType.Director},
            {"Music", PersonType.Composer},
            {"Chief Animation Direction", PersonType.Director},
            {"Series Composition", PersonType.Writer},
            {"Animation Work", PersonType.Producer},
            {"Original Work", PersonType.Writer},
            {"Character Design", PersonType.Writer},
            {"Work", PersonType.Producer},
            {"Animation Character Design", PersonType.Writer},
            {"Effects Direction", PersonType.Writer},
            {"Original Plan", PersonType.Writer},
            {"Chief Direction", PersonType.Director},
            {"Main Character Design", PersonType.Writer},
            {"Story Composition", PersonType.Writer},
            {"Magical Bushidou Musashi Design", PersonType.Writer}
        };

        private static readonly Dictionary<string, string> TagsToGenre = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"action", "Action"},
            {"adventure", "Adventure"},
            {"comedy", "Comedy"},
            {"dementia", "Dementia"},
            {"demon", "Demons"},
            {"melodrama", "Drama"},
            {"ecchi", "Ecchi"},
            {"fantasy", "Fantasy"},
            {"dark fantasy", "Fantasy"},
            {"game", "Game"},
            {"harem", "Harem"},
            {"18 restricted", "Hentai"},
            {"erotic game", "Hentai"},
            {"sex", "Hentai"},
            {"historical", "Historical"},
            {"horror", "Horror"},
            {"josei", "Josei"},
            {"magic", "Magic"},
            {"martial arts", "Martial Arts"},
            {"mecha", "Mecha"},
            {"military", "Military"},
            {"motorsport", "Motorsport"},
            {"music", "Music"},
            {"mystery", "Mystery"},
            {"parody", "Parody"},
            {"cops", "Police"},
            {"psychological", "Psychological"},
            {"romance", "Romance"},
            {"samurai", "Samurai"},
            {"school", "School"},
            {"science fiction", "Sci-Fi"},
            {"seinen", "Seinen"},
            {"shoujo", "Shoujo"},
            {"shoujo ai", "Shoujo Ai"},
            {"shounen", "Shounen"},
            {"shounen ai", "Shounen Ai"},
            {"daily life", "Slice of Life"},
            {"space", "Space"},
            {"alien", "Space"},
            {"space travel", "Space"},
            {"sports", "Sports"},
            {"super power", "Super Power"},
            {"contemporary fantasy", "Supernatural"},
            {"thriller", "Thriller"},
            {"vampire", "Vampire"},
            {"yaoi", "Yaoi"},
            {"yuri", "Yuri"}
        };

        public AniDbSeriesProvider(IApplicationPaths appPaths, IHttpClient httpClient, ILogger logger, IFileSystem fileSystem)
        {
            _appPaths = appPaths;
            _httpClient = httpClient;
            _logger = logger;
            _fileSystem = fileSystem;

            Current = this;
        }

        internal static AniDbSeriesProvider Current { get; private set; }
        public int Order => 9;

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();

            var aid = info.GetProviderId(ProviderNames.AniDb);
            if (string.IsNullOrEmpty(aid) && !string.IsNullOrEmpty(info.Name))
            {
                aid = await Fast_xml_search(info.Name, info.Name, cancellationToken).ConfigureAwait(false);
                _logger.Debug("Found aid from Fast_xml_search: {0}", aid);
                if (string.IsNullOrEmpty(aid))
                {
                    aid = await Fast_xml_search(Equals_check.Clear_name(info.Name), Equals_check.Clear_name(info.Name), cancellationToken).ConfigureAwait(false);
                    _logger.Debug("Found aid from Fast_xml_search: {0}", aid);
                }
            }

            if (!string.IsNullOrEmpty(aid))
            {
                result.Item = new Series();
                result.HasMetadata = true;

                result.Item.SetProviderId(ProviderNames.AniDb, aid);

                var seriesDataPath = await GetSeriesData(_appPaths, _httpClient, _fileSystem, aid, cancellationToken).ConfigureAwait(false);
                FetchSeriesInfo(result, seriesDataPath, info.MetadataLanguages);
            }

            return result;
        }

        public async Task<string> GetAniDbXml(CancellationToken cancellationToken)
        {
            await AniDbSeriesProvider.RequestLimiter.Tick(cancellationToken).ConfigureAwait(false);
            await Task.Delay(Plugin.Instance.Configuration.AniDB_wait_time, cancellationToken).ConfigureAwait(false);

            var options = new HttpRequestOptions
            {
                CancellationToken = cancellationToken,
                Url = "http://anidb.net/api/animetitles.xml",
                CacheLength = TimeSpan.FromDays(7),
                CacheMode = CacheMode.Unconditional,
                UserAgent = "Emby/4.0"
            };

            using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Return the AniDB ID if a and b match
        /// </summary>
        private async Task<string> Fast_xml_search(string a, string b, CancellationToken cancellationToken)
        {
            //Get AID aid=\"([s\S].*)\">
            var xml = await GetAniDbXml(cancellationToken).ConfigureAwait(false);

            _logger.Debug("Anidb xml length: {0}", xml.Length);

            List<string> pre_aid = new List<string>();
            int x = 0;
            string s1 = "-";
            string s2 = "-";
            while (!string.IsNullOrEmpty(s1) && !string.IsNullOrEmpty(s2))
            {
                s1 = Equals_check.One_line_regex(new Regex("<anime aid=" + "\"" + @"(\d+)" + "\"" + @">(?>[^<>]+|<(?!\/anime>)[^<>]*>)*?" + Regex.Escape(Equals_check.Half_string(a, 4))), xml, 1, x);
                if (s1 != "")
                {
                    pre_aid.Add(s1);
                }
                s2 = Equals_check.One_line_regex(new Regex("<anime aid=" + "\"" + @"(\d+)" + "\"" + @">(?>[^<>]+|<(?!\/anime>)[^<>]*>)*?" + Regex.Escape(Equals_check.Half_string(b, 4))), xml, 1, x);
                if (s1 != "")
                {
                    if (s1 != s2)
                    {
                        pre_aid.Add(s2);
                    }
                }
                x++;
            }

            _logger.Debug("Anidb pre_aid Count: {0}", pre_aid.Count);

            if (pre_aid.Count == 1)
            {
                if (!string.IsNullOrEmpty(pre_aid[0]))
                {
                    return pre_aid[0];
                }
            }
            int biggestcount = 0;
            string cache_aid = "";
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            {
                foreach (string _aid in pre_aid)
                {
                    string result = Equals_check.One_line_regex(new Regex(@"<anime aid=" + "\"" + _aid + "\"" + @"((?s).*?)<\/anime>"), xml);
                    int count = (result.Length - result.Replace(a, "").Length) / a.Length;
                    if (biggestcount < count)
                    {
                        biggestcount = count;
                        cache_aid = _aid;
                    }
                }
                _logger.Debug("Anidb cache_aid: {0}", cache_aid);
                if (!string.IsNullOrEmpty(cache_aid))
                {
                    return cache_aid;
                }
            }
            foreach (string _aid in pre_aid)
            {
                XElement doc = XElement.Parse("<?xml version=\"1.0\" encoding=\"UTF - 8\"?>" + "<animetitles>" + Equals_check.One_line_regex(new Regex("<anime aid=\"" + _aid + "\">" + @"(?s)(.*?)<\/anime>"), xml, 0, 0) + "</animetitles>");
                var a_ = from page in doc.Elements("anime")
                         where string.Equals(_aid, page.Attribute("aid").Value, StringComparison.OrdinalIgnoreCase)
                         select page;

                if (Simple_compare(a_.Elements("title"), b) && Simple_compare(a_.Elements("title"), a))
                {
                    return _aid;
                }
            }
            return "";
        }

        /// <summary>
        /// Simple Compare a XElemtent with a string
        /// </summary>
        /// <param name="a_"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private static bool Simple_compare(IEnumerable<XElement> a_, string b)
        {
            bool ignore_date = true;
            string a_date = "";
            string b_date = "";

            string b_date_ = Equals_check.One_line_regex(new Regex(@"([0-9][0-9][0-9][0-9])"), b);
            if (!string.IsNullOrEmpty(b_date_))
            {
                b_date = b_date_;
            }
            if (!string.IsNullOrEmpty(b_date))
            {
                foreach (XElement a in a_)
                {
                    if (ignore_date)
                    {
                        string a_date_ = Equals_check.One_line_regex(new Regex(@"([0-9][0-9][0-9][0-9])"), a.Value);
                        if (!string.IsNullOrEmpty(a_date_))
                        {
                            a_date = a_date_;
                            ignore_date = false;
                        }
                    }
                }
            }
            if (!ignore_date)
            {
                if (string.Equals(a_date.Trim(), b_date.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    foreach (XElement a in a_)
                    {
                        if (Equals_check.Simple_compare(a.Value, b, true))
                            return true;
                    }
                }
                else
                {
                    return false;
                }
                return false;
            }
            else
            {
                foreach (XElement a in a_)
                {
                    if (ignore_date)
                    {
                        if (Equals_check.Simple_compare(a.Value, b, true))
                            return true;
                    }
                }
                return false;
            }
        }

        public string Name => "AniDB";

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            var metadata = await GetMetadata(searchInfo, cancellationToken).ConfigureAwait(false);

            var list = new List<RemoteSearchResult>();

            if (metadata.HasMetadata)
            {
                var res = new RemoteSearchResult
                {
                    Name = metadata.Item.Name,
                    PremiereDate = metadata.Item.PremiereDate,
                    ProductionYear = metadata.Item.ProductionYear,
                    ProviderIds = metadata.Item.ProviderIds,
                    SearchProviderName = Name
                };

                list.Add(res);
            }

            return list;
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClient.GetResponse(new HttpRequestOptions
            {
                CancellationToken = cancellationToken,
                Url = url
            });
        }

        public static async Task<string> GetSeriesData(IApplicationPaths appPaths, IHttpClient httpClient, IFileSystem fileSystem, string seriesId, CancellationToken cancellationToken)
        {
            var dataPath = CalculateSeriesDataPath(appPaths, seriesId);
            var seriesDataPath = Path.Combine(dataPath, SeriesDataFile);
            var fileInfo = fileSystem.GetFileInfo(seriesDataPath);

            // download series data if not present, or out of date
            if (!fileInfo.Exists || DateTime.UtcNow - fileInfo.LastWriteTimeUtc > TimeSpan.FromDays(7))
            {
                await DownloadSeriesData(seriesId, seriesDataPath, appPaths.CachePath, httpClient, fileSystem, cancellationToken).ConfigureAwait(false);
            }

            return seriesDataPath;
        }

        public static string CalculateSeriesDataPath(IApplicationPaths paths, string seriesId)
        {
            return Path.Combine(paths.CachePath, "anidb", "series", seriesId);
        }

        private void FetchSeriesInfo(MetadataResult<Series> result, string seriesDataPath, CultureDto[] metadataLanguages)
        {
            var series = result.Item;
            var settings = new XmlReaderSettings
            {
                CheckCharacters = false,
                IgnoreProcessingInstructions = true,
                IgnoreComments = true,
                ValidationType = ValidationType.None
            };

            using (var streamReader = _fileSystem.GetFileStream(seriesDataPath, FileOpenMode.Open, FileAccessMode.Read))
            using (var reader = XmlReader.Create(streamReader, settings))
            {
                reader.MoveToContent();

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        switch (reader.Name)
                        {
                            case "startdate":
                                var val = reader.ReadElementContentAsString();

                                if (!string.IsNullOrWhiteSpace(val))
                                {

                                    if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime date))
                                    {
                                        date = date.ToUniversalTime();
                                        series.PremiereDate = date;
                                        series.ProductionYear = date.Year;
                                    }
                                }

                                break;

                            case "enddate":
                                var endDate = reader.ReadElementContentAsString();

                                if (!string.IsNullOrWhiteSpace(endDate))
                                {
                                    if (DateTime.TryParse(endDate, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime date))
                                    {
                                        date = date.ToUniversalTime();
                                        series.EndDate = date;
                                        if (DateTime.Now.Date < date.Date)
                                        {
                                            series.Status = SeriesStatus.Continuing;
                                        }
                                        else
                                        {
                                            series.Status = SeriesStatus.Ended;
                                        }
                                    }
                                }

                                break;

                            case "titles":
                                using (var subtree = reader.ReadSubtree())
                                {
                                    var title = ParseTitle(subtree, metadataLanguages);
                                    if (!string.IsNullOrEmpty(title))
                                    {
                                        series.Name = title;
                                    }
                                }

                                break;

                            case "creators":
                                using (var subtree = reader.ReadSubtree())
                                {
                                    ParseCreators(result, subtree);
                                }

                                break;

                            case "description":
                                series.Overview = ReplaceLineFeedWithNewLine(StripAniDbLinks(reader.ReadElementContentAsString()).Split(new[] { "Source:", "Note:" }, StringSplitOptions.None)[0]);

                                break;

                            case "ratings":
                                using (var subtree = reader.ReadSubtree())
                                {
                                    ParseRatings(series, subtree);
                                }

                                break;

                            case "resources":
                                using (var subtree = reader.ReadSubtree())
                                {
                                    ParseResources(series, subtree);
                                }

                                break;

                            case "characters":
                                using (var subtree = reader.ReadSubtree())
                                {
                                    ParseActors(result, subtree);
                                }

                                break;

                            case "tags":
                                using (var subtree = reader.ReadSubtree())
                                {
                                    ParseTags(series, subtree);
                                }

                                break;

                            case "categories":
                                using (var subtree = reader.ReadSubtree())
                                {
                                }

                                break;

                            case "episodes":
                                using (var subtree = reader.ReadSubtree())
                                {
                                    ParseEpisodes(series, subtree);
                                }

                                break;
                        }
                    }
                }
            }
            if (series.EndDate == null)
            {
                series.Status = SeriesStatus.Continuing;
            }

            GenreHelper.CleanupGenres(series);
        }

        private void ParseEpisodes(Series series, XmlReader reader)
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && string.Equals(reader.Name, "episode", StringComparison.OrdinalIgnoreCase))
                {

                    if (int.TryParse(reader.GetAttribute("id"), out int id) && IgnoredCategoryIds.Contains(id))
                        continue;

                    using (var episodeSubtree = reader.ReadSubtree())
                    {
                        while (episodeSubtree.Read())
                        {
                            if (episodeSubtree.NodeType == XmlNodeType.Element)
                            {
                                switch (episodeSubtree.Name)
                                {
                                    case "epno":
                                        //var epno = episodeSubtree.ReadElementContentAsString();
                                        //EpisodeInfo info = new EpisodeInfo();
                                        //info.AnimeSeriesIndex = series.AnimeSeriesIndex;
                                        //info.IndexNumberEnd = string(epno);
                                        //info.SeriesProviderIds.GetOrDefault(ProviderNames.AniDb);
                                        //episodes.Add(info);
                                        break;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ParseTags(Series series, XmlReader reader)
        {
            var genres = new List<GenreInfo>();

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && string.Equals(reader.Name, "tag", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(reader.GetAttribute("weight"), out int weight))
                        continue;

                    if (int.TryParse(reader.GetAttribute("id"), out int id) && IgnoredCategoryIds.Contains(id))
                        continue;

                    if (int.TryParse(reader.GetAttribute("parentid"), out int parentId) && IgnoredCategoryIds.Contains(parentId))
                        continue;

                    using (var categorySubtree = reader.ReadSubtree())
                    {
                        PluginConfiguration config = Plugin.Instance.Configuration;
                        while (categorySubtree.Read())
                        {
                            if (categorySubtree.NodeType == XmlNodeType.Element && string.Equals(categorySubtree.Name, "name", StringComparison.OrdinalIgnoreCase))
                            {
                                /*
                                 * Since AniDB tagging (and weight) system is really messy additional TagsToGenre conversion was added. This method adds matching genre regardless of its weight.
                                 * 
                                 * If tags are not converted weight limitation works as in previous plugin versions (<=1.3.5)
                                 */
                                if (config.TidyGenreList)
                                {
                                    var name = categorySubtree.ReadElementContentAsString();
                                    if (TagsToGenre.TryGetValue(name, out string mapped))
                                        genres.Add(new GenreInfo { Name = mapped, Weight = weight });
                                }
                                else if (weight >= 400)
                                {
                                    var name = categorySubtree.ReadElementContentAsString();
                                    genres.Add(new GenreInfo { Name = UpperCase(name), Weight = weight });
                                }
                            }
                        }
                    }
                }
            }

            series.Genres = genres.OrderBy(g => g.Weight).Select(g => g.Name).ToArray();
        }

        private void ParseResources(Series series, XmlReader reader)
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && string.Equals(reader.Name, "resource", StringComparison.OrdinalIgnoreCase))
                {
                    var type = reader.GetAttribute("type");

                    switch (type)
                    {
                        case "2":
                            var ids = new List<int>();

                            using (var idSubtree = reader.ReadSubtree())
                            {
                                while (idSubtree.Read())
                                {
                                    if (idSubtree.NodeType == XmlNodeType.Element && idSubtree.Name == "identifier")
                                    {
                                        if (int.TryParse(idSubtree.ReadElementContentAsString(), out int id))
                                            ids.Add(id);
                                    }
                                }
                            }

                            if (ids.Count > 0)
                            {
                                var firstId = ids.OrderBy(i => i).First().ToString(CultureInfo.InvariantCulture);
                                series.SetProviderId(ProviderNames.MyAnimeList, firstId);
                                //                                series.ProviderIds.Add(ProviderNames.AniList, firstId);
                            }

                            break;

                        case "4":
                            while (reader.Read())
                            {
                                if (reader.NodeType == XmlNodeType.Element && string.Equals(reader.Name, "url", StringComparison.OrdinalIgnoreCase))
                                {
                                    reader.ReadElementContentAsString();
                                    break;
                                }
                            }

                            break;
                    }
                }
            }
        }

        private static string UpperCase(string value)
        {
            char[] array = value.ToCharArray();
            if (array.Length >= 1)
                if (char.IsLower(array[0]))
                    array[0] = char.ToUpper(array[0]);

            for (int i = 1; i < array.Length; i++)
                if (array[i - 1] == ' ' || array[i - 1] == '-')
                    if (char.IsLower(array[i]))
                        array[i] = char.ToUpper(array[i]);

            return new string(array);
        }

        public static string StripAniDbLinks(string text)
        {
            return AniDbUrlRegex.Replace(text, "${name}");
        }

        public static string ReplaceLineFeedWithNewLine(string text)
        {
            return text.Replace("\n", "<br>\n");
        }

        private void ParseActors(MetadataResult<Series> series, XmlReader reader)
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (string.Equals(reader.Name, "character", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var subtree = reader.ReadSubtree())
                        {
                            ParseActor(series, subtree);
                        }
                    }
                }
            }
        }

        private void ParseActor(MetadataResult<Series> series, XmlReader reader)
        {
            string name = null;
            string role = null;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "name":
                            role = reader.ReadElementContentAsString();
                            break;

                        case "seiyuu":
                            name = reader.ReadElementContentAsString();
                            break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(role)) // && series.People.All(p => p.Name != name))
            {
                series.AddPerson(CreatePerson(name, PersonType.Actor, role));
            }
        }

        private void ParseRatings(Series series, XmlReader reader)
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (string.Equals(reader.Name, "permanent", StringComparison.OrdinalIgnoreCase))
                    {

                        if (float.TryParse(
                        reader.ReadElementContentAsString(),
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out float rating))
                        {
                            series.CommunityRating = (float)Math.Round(rating, 1);
                        }
                    }
                }
            }
        }

        private string ParseTitle(XmlReader reader, CultureDto[] metadataLanguages)
        {
            var titles = new List<Title>();

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && string.Equals(reader.Name, "title", StringComparison.OrdinalIgnoreCase))
                {
                    var language = reader.GetAttribute("xml:lang");
                    var type = reader.GetAttribute("type");
                    var name = reader.ReadElementContentAsString();

                    titles.Add(new Title
                    {
                        Language = language,
                        Type = type,
                        Name = name
                    });
                }
            }

            return titles.Localize(metadataLanguages).Name;
        }

        private void ParseCreators(MetadataResult<Series> series, XmlReader reader)
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && string.Equals(reader.Name, "name", StringComparison.OrdinalIgnoreCase))
                {
                    var type = reader.GetAttribute("type");
                    var name = reader.ReadElementContentAsString();

                    if (type == "Animation Work" || type == "Work")
                    {
                        series.Item.AddStudio(name);
                    }
                    else
                    {
                        series.AddPerson(CreatePerson(name, type));
                    }
                }
            }
        }

        private PersonInfo CreatePerson(string name, string type, string role = null)
        {
            // todo find nationality of person and conditionally reverse name order
            PersonType mappedType;

            if (!_typeMappings.TryGetValue(type, out mappedType))
            {
                if (!Enum.TryParse(type, true, out mappedType))
                {
                    mappedType = PersonType.Actor;
                }
            }

            return new PersonInfo
            {
                Name = ReverseNameOrder(name),
                Type = mappedType,
                Role = role
            };
        }

        private PersonInfo CreatePerson(string name, PersonType type, string role = null)
        {
            return new PersonInfo
            {
                Name = ReverseNameOrder(name),
                Type = type,
                Role = role
            };
        }

        public static string ReverseNameOrder(string name)
        {
            return name.Split(' ').Reverse().Aggregate(string.Empty, (n, part) => n + " " + part).Trim();
        }

        private static async Task DownloadSeriesData(string aid, string seriesDataPath, string cachePath, IHttpClient httpClient, IFileSystem fileSystem, CancellationToken cancellationToken)
        {
            var directory = fileSystem.GetDirectoryName(seriesDataPath);
            if (directory != null)
            {
                fileSystem.CreateDirectory(directory);
            }

            DeleteXmlFiles(directory, fileSystem);

            var requestOptions = new HttpRequestOptions
            {
                Url = string.Format(SeriesQueryUrl, ClientName, aid),
                CancellationToken = cancellationToken,
                EnableHttpCompression = false
            };

            await RequestLimiter.Tick(cancellationToken);
            await Task.Delay(Plugin.Instance.Configuration.AniDB_wait_time, cancellationToken).ConfigureAwait(false);
            using (var stream = await httpClient.Get(requestOptions).ConfigureAwait(false))
            using (var unzipped = new GZipStream(stream, CompressionMode.Decompress))
            using (var reader = new StreamReader(unzipped, Encoding.UTF8, true))
            using (var file = fileSystem.GetFileStream(seriesDataPath, FileOpenMode.Create, FileAccessMode.Write))
            using (var writer = new StreamWriter(file))
            {
                var text = await reader.ReadToEndAsync().ConfigureAwait(false);
                text = text.Replace("&#x0;", "");

                await writer.WriteAsync(text).ConfigureAwait(false);
            }

            await ExtractEpisodes(directory, seriesDataPath, fileSystem).ConfigureAwait(false);
            ExtractCast(cachePath, seriesDataPath, fileSystem);
        }

        private static void DeleteXmlFiles(string path, IFileSystem fileSystem)
        {
            try
            {
                foreach (var file in fileSystem.GetFilePaths(path, true)
                    .ToList())
                {
                    fileSystem.DeleteFile(file);
                }
            }
            catch (DirectoryNotFoundException)
            {
                // No biggie
            }
        }

        private static async Task ExtractEpisodes(string seriesDataDirectory, string seriesDataPath, IFileSystem fileSystem)
        {
            var settings = new XmlReaderSettings
            {
                CheckCharacters = false,
                IgnoreProcessingInstructions = true,
                IgnoreComments = true,
                ValidationType = ValidationType.None
            };

            using (var fileStream = fileSystem.OpenRead(seriesDataPath))
            {
                using (var streamReader = new StreamReader(fileStream, Encoding.UTF8))
                {
                    // Use XmlReader for best performance
                    using (var reader = XmlReader.Create(streamReader, settings))
                    {
                        reader.MoveToContent();

                        // Loop through each element
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element)
                            {
                                if (reader.Name == "episode")
                                {
                                    var outerXml = reader.ReadOuterXml();
                                    await SaveEpsiodeXml(seriesDataDirectory, outerXml).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void ExtractCast(string cachePath, string seriesDataPath, IFileSystem fileSystem)
        {
            var settings = new XmlReaderSettings
            {
                CheckCharacters = false,
                IgnoreProcessingInstructions = true,
                IgnoreComments = true,
                ValidationType = ValidationType.None
            };

            var cast = new List<AniDbPersonInfo>();

            using (var fileStream = fileSystem.OpenRead(seriesDataPath))
            {
                using (var streamReader = new StreamReader(fileStream, Encoding.UTF8))
                {
                    // Use XmlReader for best performance
                    using (var reader = XmlReader.Create(streamReader, settings))
                    {
                        reader.MoveToContent();

                        // Loop through each element
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element && string.Equals(reader.Name, "characters", StringComparison.OrdinalIgnoreCase))
                            {
                                var outerXml = reader.ReadOuterXml();
                                cast.AddRange(ParseCharacterList(outerXml));
                            }

                            if (reader.NodeType == XmlNodeType.Element && string.Equals(reader.Name, "creators", StringComparison.OrdinalIgnoreCase))
                            {
                                var outerXml = reader.ReadOuterXml();
                                cast.AddRange(ParseCreatorsList(outerXml));
                            }
                        }
                    }
                }
            }

            var serializer = new XmlSerializer(typeof(AniDbPersonInfo));
            foreach (var person in cast)
            {
                var path = GetCastPath(person.Name, cachePath);
                var directory = fileSystem.GetDirectoryName(path);
                fileSystem.CreateDirectory(directory);

                if (!fileSystem.FileExists(path) || person.Image != null)
                {
                    try
                    {
                        using (var stream = fileSystem.GetFileStream(path, FileOpenMode.Create, FileAccessMode.Write))
                            serializer.Serialize(stream, person);
                    }
                    catch (IOException)
                    {
                        // ignore
                    }
                }
            }
        }

        public static AniDbPersonInfo GetPersonInfo(string cachePath, string name, IFileSystem fileSystem)
        {
            var path = GetCastPath(name, cachePath);
            var serializer = new XmlSerializer(typeof(AniDbPersonInfo));

            try
            {
                if (fileSystem.FileExists(path))
                {
                    using (var stream = fileSystem.OpenRead(path))
                        return serializer.Deserialize(stream) as AniDbPersonInfo;
                }
            }
            catch (IOException)
            {
                return null;
            }

            return null;
        }

        private static string GetCastPath(string name, string cachePath)
        {
            name = name.ToLowerInvariant();
            return Path.Combine(cachePath, "anidb-people", name[0].ToString(), name + ".xml");
        }

        private static IEnumerable<AniDbPersonInfo> ParseCharacterList(string xml)
        {
            var doc = XDocument.Parse(xml);
            var people = new List<AniDbPersonInfo>();

            var characters = doc.Element("characters");
            if (characters != null)
            {
                foreach (var character in characters.Descendants("character"))
                {
                    var seiyuu = character.Element("seiyuu");
                    if (seiyuu != null)
                    {
                        var person = new AniDbPersonInfo
                        {
                            Name = ReverseNameOrder(seiyuu.Value)
                        };

                        var picture = seiyuu.Attribute("picture");
                        if (picture != null && !string.IsNullOrEmpty(picture.Value))
                        {
                            person.Image = "http://img7.anidb.net/pics/anime/" + picture.Value;
                        }

                        var id = seiyuu.Attribute("id");
                        if (id != null && !string.IsNullOrEmpty(id.Value))
                        {
                            person.Id = id.Value;
                        }

                        people.Add(person);
                    }
                }
            }

            return people;
        }

        private static IEnumerable<AniDbPersonInfo> ParseCreatorsList(string xml)
        {
            var doc = XDocument.Parse(xml);
            var people = new List<AniDbPersonInfo>();

            var creators = doc.Element("creators");
            if (creators != null)
            {
                foreach (var creator in creators.Descendants("name"))
                {
                    var type = creator.Attribute("type");
                    if (type != null && type.Value == "Animation Work")
                    {
                        continue;
                    }

                    var person = new AniDbPersonInfo
                    {
                        Name = ReverseNameOrder(creator.Value)
                    };

                    var id = creator.Attribute("id");
                    if (id != null && !string.IsNullOrEmpty(id.Value))
                    {
                        person.Id = id.Value;
                    }

                    people.Add(person);
                }
            }

            return people;
        }

        private static async Task SaveXml(string xml, string filename)
        {
            var writerSettings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Async = true
            };

            using (var writer = XmlWriter.Create(filename, writerSettings))
            {
                await writer.WriteRawAsync(xml).ConfigureAwait(false);
            }
        }

        private static async Task SaveEpsiodeXml(string seriesDataDirectory, string xml)
        {
            var episodeNumber = ParseEpisodeNumber(xml);

            if (episodeNumber != null)
            {
                var file = Path.Combine(seriesDataDirectory, string.Format("episode-{0}.xml", episodeNumber));
                await SaveXml(xml, file).ConfigureAwait(false);
            }
        }

        private static string ParseEpisodeNumber(string xml)
        {
            var settings = new XmlReaderSettings
            {
                CheckCharacters = false,
                IgnoreProcessingInstructions = true,
                IgnoreComments = true,
                ValidationType = ValidationType.None
            };

            using (var streamReader = new StringReader(xml))
            {
                // Use XmlReader for best performance
                using (var reader = XmlReader.Create(streamReader, settings))
                {
                    reader.MoveToContent();

                    // Loop through each element
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            if (reader.Name == "epno")
                            {
                                var val = reader.ReadElementContentAsString();
                                if (!string.IsNullOrWhiteSpace(val))
                                {
                                    return val;
                                }
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        ///     Gets the series data path.
        /// </summary>
        /// <param name="appPaths">The app paths.</param>
        /// <param name="seriesId">The series id.</param>
        /// <returns>System.String.</returns>
        internal static string GetSeriesDataPath(IApplicationPaths appPaths, string seriesId)
        {
            var seriesDataPath = Path.Combine(GetSeriesDataPath(appPaths), seriesId);

            return seriesDataPath;
        }

        /// <summary>
        ///     Gets the series data path.
        /// </summary>
        /// <param name="appPaths">The app paths.</param>
        /// <returns>System.String.</returns>
        internal static string GetSeriesDataPath(IApplicationPaths appPaths)
        {
            var dataPath = Path.Combine(appPaths.CachePath, "anidb\\series");

            return dataPath;
        }

        private struct GenreInfo
        {
            public string Name;
            public int Weight;
        }
    }

    public class Title
    {
        public string Language { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
    }

    public static class TitleExtensions
    {
        public static Title Localize(this List<Title> titles, CultureDto[] metadataLanguages)
        {
            var titlesMatchingLanguage = titles
                .Where(i => string.IsNullOrEmpty(i.Language) || metadataLanguages.Any(m => m.ContainsLanguage(i.Language)))
                .OrderBy(i => Array.FindIndex(metadataLanguages, m => m.ContainsLanguage(i.Language)) * -10000)
                .ToList();

            // prefer an official title, else look for a synonym
            var localized = titlesMatchingLanguage.FirstOrDefault(t => string.Equals(t.Type, "main", StringComparison.OrdinalIgnoreCase)) ??
                            titlesMatchingLanguage.FirstOrDefault(t => string.Equals(t.Type, "official", StringComparison.OrdinalIgnoreCase)) ??
                            titlesMatchingLanguage.FirstOrDefault(t => string.Equals(t.Type, "synonym", StringComparison.OrdinalIgnoreCase));

            if (localized != null)
            {
                return localized;
            }

            // return the main title (romaji)
            return titles.FirstOrDefault(t => string.Equals(t.Type, "main", StringComparison.OrdinalIgnoreCase)) ??
                titles.FirstOrDefault(t => string.Equals(t.Type, "official", StringComparison.OrdinalIgnoreCase)) ??
                titles.FirstOrDefault();
        }

        /// <summary>
        ///     Gets the series data path.
        /// </summary>
        /// <param name="appPaths">The app paths.</param>
        /// <param name="seriesId">The series id.</param>
        /// <returns>System.String.</returns>
        internal static string GetSeriesDataPath(IApplicationPaths appPaths, string seriesId)
        {
            var seriesDataPath = Path.Combine(GetSeriesDataPath(appPaths), seriesId);

            return seriesDataPath;
        }

        /// <summary>
        ///     Gets the series data path.
        /// </summary>
        /// <param name="appPaths">The app paths.</param>
        /// <returns>System.String.</returns>
        internal static string GetSeriesDataPath(IApplicationPaths appPaths)
        {
            var dataPath = Path.Combine(appPaths.CachePath, "tvdb");

            return dataPath;
        }
    }
}