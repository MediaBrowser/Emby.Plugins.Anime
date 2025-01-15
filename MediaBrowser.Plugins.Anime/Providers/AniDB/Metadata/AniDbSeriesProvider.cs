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
        private static readonly Regex AniDbUrlRegex = new Regex(@"http://anidb.net/\w+ \[(?<name>[^\]]*)\]", RegexOptions.IgnoreCase);
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
        }

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
                using (var stream = await GetSeriesDataFile(_appPaths, _httpClient, _fileSystem, aid, cancellationToken).ConfigureAwait(false))
                {
                    if (stream != null)
                    {
                        result.Item = new Series();
                        result.HasMetadata = true;

                        result.Item.SetProviderId(ProviderNames.AniDb, aid);

                        FetchSeriesInfo(result, stream, info.MetadataLanguages);
                    }
                }
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
                s1 = Equals_check.One_line_regex(new Regex("<anime aid=" + "\"" + @"(\d+)" + "\"" + @">(?>[^<>]+|<(?!\/anime>)[^<>]*>)*?" + Regex.Escape(Equals_check.Half_string(a, 4)), RegexOptions.IgnoreCase), xml, 1, x);
                if (!string.IsNullOrEmpty(s1))
                {
                    pre_aid.Add(s1);
                }
                s2 = Equals_check.One_line_regex(new Regex("<anime aid=" + "\"" + @"(\d+)" + "\"" + @">(?>[^<>]+|<(?!\/anime>)[^<>]*>)*?" + Regex.Escape(Equals_check.Half_string(b, 4)), RegexOptions.IgnoreCase), xml, 1, x);
                if (!string.IsNullOrEmpty(s1))
                {
                    if (!string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase))
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
                    string result = Equals_check.One_line_regex(new Regex(@"<anime aid=" + "\"" + _aid + "\"" + @"((?s).*?)<\/anime>", RegexOptions.IgnoreCase), xml);
                    int count = (result.Length - result.Replace(a, "", StringComparison.OrdinalIgnoreCase).Length) / a.Length;
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
                XElement doc = XElement.Parse("<?xml version=\"1.0\" encoding=\"UTF - 8\"?>" + "<animetitles>" + Equals_check.One_line_regex(new Regex("<anime aid=\"" + _aid + "\">" + @"(?s)(.*?)<\/anime>", RegexOptions.IgnoreCase), xml, 0, 0) + "</animetitles>");
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

            string b_date_ = Equals_check.One_line_regex(new Regex(@"([0-9][0-9][0-9][0-9])", RegexOptions.IgnoreCase), b);
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
                        string a_date_ = Equals_check.One_line_regex(new Regex(@"([0-9][0-9][0-9][0-9])", RegexOptions.IgnoreCase), a.Value);
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

        private static async Task<string> GetSeriesData(IApplicationPaths appPaths, IHttpClient httpClient, IFileSystem fileSystem, string seriesId, CancellationToken cancellationToken)
        {
            var dataPath = CalculateSeriesDataPath(appPaths, seriesId);
            var seriesDataPath = Path.Combine(dataPath, SeriesDataFile);
            var fileInfo = fileSystem.GetFileInfo(seriesDataPath);

            // download series data if not present, or out of date
            if (!fileInfo.Exists || (DateTime.UtcNow - fileInfo.LastWriteTimeUtc) > TimeSpan.FromDays(3))
            {
                await DownloadSeriesData(seriesId, seriesDataPath, httpClient, fileSystem, cancellationToken).ConfigureAwait(false);
            }

            return seriesDataPath;
        }

        public static async Task<Stream> GetSeriesDataFile(IApplicationPaths appPaths, IHttpClient httpClient, IFileSystem fileSystem, string seriesId, CancellationToken cancellationToken)
        {
            var path = await GetSeriesData(appPaths, httpClient, fileSystem, seriesId, cancellationToken).ConfigureAwait(false);

            return fileSystem.OpenRead(path);
        }

        public static string CalculateSeriesDataPath(IApplicationPaths paths, string seriesId)
        {
            return Path.Combine(paths.CachePath, "anidb", "series", seriesId);
        }

        private void FetchSeriesInfo(MetadataResult<Series> result, Stream seriesData, CultureDto[] metadataLanguages)
        {
            var series = result.Item;
            var settings = new XmlReaderSettings
            {
                CheckCharacters = false,
                IgnoreProcessingInstructions = true,
                IgnoreComments = true,
                ValidationType = ValidationType.None
            };

            using (var reader = XmlReader.Create(seriesData, settings))
            {
                reader.MoveToContent();
                reader.Read();

                while (!reader.EOF && reader.ReadState == ReadState.Interactive)
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
                            default:
                                reader.Skip();
                                break;
                        }
                    }
                    else
                    {
                        reader.Read();
                    }
                }
            }
            if (series.EndDate == null)
            {
                series.Status = SeriesStatus.Continuing;
            }

            GenreHelper.CleanupGenres(series);
        }

        private void ParseTags(Series series, XmlReader reader)
        {
            var genres = new List<GenreInfo>();

            reader.MoveToContent();
            reader.Read();

            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "tag":
                            if (!int.TryParse(reader.GetAttribute("weight"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight))
                                continue;

                            if (int.TryParse(reader.GetAttribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && IgnoredCategoryIds.Contains(id))
                                continue;

                            if (int.TryParse(reader.GetAttribute("parentid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parentId) && IgnoredCategoryIds.Contains(parentId))
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
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
                else
                {
                    reader.Read();
                }
            }

            series.Genres = genres.OrderBy(g => g.Weight).Select(g => g.Name).ToArray();
        }

        private void ParseResources(Series series, XmlReader reader)
        {
            reader.MoveToContent();
            reader.Read();

            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "resource":
                            var type = reader.GetAttribute("type");

                            switch (type)
                            {
                                case "2":
                                    var ids = new List<int>();

                                    using (var idSubtree = reader.ReadSubtree())
                                    {
                                        while (idSubtree.Read())
                                        {
                                            if (idSubtree.NodeType == XmlNodeType.Element && string.Equals(idSubtree.Name, "identifier", StringComparison.OrdinalIgnoreCase))
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
                                default:
                                    reader.Skip();
                                    break;
                            }
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
                else
                {
                    reader.Read();
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
            return text.Replace("\n", "<br>\n", StringComparison.OrdinalIgnoreCase);
        }

        private void ParseActors(MetadataResult<Series> series, XmlReader reader)
        {
            reader.MoveToContent();
            reader.Read();

            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "character":
                            using (var subtree = reader.ReadSubtree())
                            {
                                ParseActor(series, subtree);
                            }
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
                else
                {
                    reader.Read();
                }
            }
        }

        private void ParseActor(MetadataResult<Series> series, XmlReader reader)
        {
            string name = null;
            string role = null;
            string imageUrl = null;
            string id = null;

            reader.MoveToContent();
            reader.Read();

            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "name":
                            role = reader.ReadElementContentAsString();
                            break;

                        case "seiyuu":

                            var picture = reader.GetAttribute("picture");
                            if (!string.IsNullOrEmpty(picture))
                            {
                                imageUrl = "http://img7.anidb.net/pics/anime/" + picture;
                            }

                            id = reader.GetAttribute("id");

                            name = reader.ReadElementContentAsString();
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
                else
                {
                    reader.Read();
                }
            }

            if (!string.IsNullOrEmpty(name))
            {
                var personInfo = new PersonInfo
                {
                    Name = ReverseNameOrder(name),
                    Type = PersonType.Actor,
                    Role = role,
                    ImageUrl = imageUrl
                };

                if (!string.IsNullOrEmpty(id))
                {
                    personInfo.SetProviderId(ProviderNames.AniDb, id);
                }

                series.AddPerson(personInfo);
            }
        }

        private void ParseRatings(Series series, XmlReader reader)
        {
            reader.MoveToContent();
            reader.Read();

            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "permanent":
                            if (float.TryParse(
                            reader.ReadElementContentAsString(),
                            NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture,
                            out float rating))
                            {
                                series.CommunityRating = (float)Math.Round(rating, 1);
                            }
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
                else
                {
                    reader.Read();
                }
            }
        }

        private string ParseTitle(XmlReader reader, CultureDto[] metadataLanguages)
        {
            var titles = new List<Title>();

            reader.MoveToContent();
            reader.Read();

            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "title":
                            var language = reader.GetAttribute("xml:lang");
                            var type = reader.GetAttribute("type");
                            var name = reader.ReadElementContentAsString();

                            titles.Add(new Title
                            {
                                Language = language,
                                Type = type,
                                Name = name
                            });
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
                else
                {
                    reader.Read();
                }
            }

            return titles.Localize(metadataLanguages, Plugin.Instance.Configuration.PreferredTitleLanguage).Name;
        }

        private void ParseCreators(MetadataResult<Series> series, XmlReader reader)
        {
            reader.MoveToContent();
            reader.Read();

            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    string id = null;
                    string imageUrl = null;

                    switch (reader.Name)
                    {
                        case "name":
                            var type = reader.GetAttribute("type");

                            var picture = reader.GetAttribute("picture");
                            if (!string.IsNullOrEmpty(picture))
                            {
                                imageUrl = "http://img7.anidb.net/pics/anime/" + picture;
                            }

                            id = reader.GetAttribute("id");

                            var name = reader.ReadElementContentAsString();

                            if (string.Equals(type, "Animation Work", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Work", StringComparison.OrdinalIgnoreCase))
                            {
                                series.Item.AddStudio(name);
                            }
                            else
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

                                var personInfo = new PersonInfo
                                {
                                    Name = ReverseNameOrder(name),
                                    Type = mappedType,
                                    ImageUrl = imageUrl
                                };

                                if (!string.IsNullOrEmpty(id))
                                {
                                    personInfo.SetProviderId(ProviderNames.AniDb, id);
                                }

                                series.AddPerson(personInfo);
                            }
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
                else
                {
                    reader.Read();
                }
            }
        }

        public static string ReverseNameOrder(string name)
        {
            return name.Split(' ').Reverse().Aggregate(string.Empty, (n, part) => n + " " + part).Trim();
        }

        private static async Task DownloadSeriesData(string aid, string seriesDataPath, IHttpClient httpClient, IFileSystem fileSystem, CancellationToken cancellationToken)
        {
            fileSystem.CreateDirectory(fileSystem.GetDirectoryName(seriesDataPath));

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
                text = text.Replace("&#x0;", "", StringComparison.OrdinalIgnoreCase);

                await writer.WriteAsync(text).ConfigureAwait(false);
            }
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
        public static Title Localize(this List<Title> titles, CultureDto[] metadataLanguages, TitleLanguageOption languageOption)
        {
            if (languageOption == TitleLanguageOption.Romaji)
            {
                var romaji = titles.FirstOrDefault(t => string.Equals(t.Language, "x-jat", StringComparison.OrdinalIgnoreCase) && string.Equals(t.Type, "main", StringComparison.OrdinalIgnoreCase));
                if (romaji != null)
                {
                    return romaji;
                }
            }

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
    }
}