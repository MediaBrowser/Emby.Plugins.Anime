using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace MediaBrowser.Plugins.Anime.Providers.AniDB.Metadata
{
    /// <summary>
    ///     The <see cref="AniDbEpisodeProvider" /> class provides episode metadata from AniDB.
    /// </summary>
    public class AniDbEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        private readonly IServerConfigurationManager _configurationManager;
        private readonly IHttpClient _httpClient;
        private readonly IFileSystem _fileSystem;

        /// <summary>
        ///     Creates a new instance of the <see cref="AniDbEpisodeProvider" /> class.
        /// </summary>
        /// <param name="configurationManager">The configuration manager.</param>
        /// <param name="httpClient">The HTTP client.</param>
        public AniDbEpisodeProvider(IServerConfigurationManager configurationManager, IHttpClient httpClient, IFileSystem fileSystem)
        {
            _configurationManager = configurationManager;
            _httpClient = httpClient;
            _fileSystem = fileSystem;
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>();

            cancellationToken.ThrowIfCancellationRequested();

            var anidbId = info.SeriesProviderIds.GetOrDefault(ProviderNames.AniDb);
            if (string.IsNullOrEmpty(anidbId))
                return result;

            var seriesFolder = await FindSeriesFolder(anidbId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(seriesFolder))
                return result;

            var xml = GetEpisodeXmlFile(info.IndexNumber, info.ParentIndexNumber, seriesFolder);
            if (xml == null || !_fileSystem.FileExists(xml))
                return result;

            result.Item = new Episode
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber
            };

            result.HasMetadata = true;

            var metadataLanguages = info.MetadataLanguages;

            ParseEpisodeXml(xml, result.Item, metadataLanguages);

            if (info.IndexNumberEnd != null && info.IndexNumberEnd > info.IndexNumber)
            {
                for (var i = info.IndexNumber + 1; i <= info.IndexNumberEnd; i++)
                {
                    var additionalXml = GetEpisodeXmlFile(i, info.ParentIndexNumber, seriesFolder);
                    if (additionalXml == null || !_fileSystem.FileExists(additionalXml))
                        continue;

                    ParseAdditionalEpisodeXml(additionalXml, result.Item, metadataLanguages);
                }
            }

            return result;
        }

        public string Name => "AniDB";

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var list = new List<RemoteSearchResult>();

            var anidbId = searchInfo.SeriesProviderIds.GetOrDefault(ProviderNames.AniDb);
            if (string.IsNullOrEmpty(anidbId))
                return list;

            await AniDbSeriesProvider.GetSeriesData(_configurationManager.ApplicationPaths, _httpClient, _fileSystem, anidbId, cancellationToken).ConfigureAwait(false);

            var metadataResult = await GetMetadata(searchInfo, cancellationToken).ConfigureAwait(false);

            if (metadataResult.HasMetadata)
            {
                var item = metadataResult.Item;

                list.Add(new RemoteSearchResult
                {
                    IndexNumber = item.IndexNumber,
                    Name = item.Name,
                    ParentIndexNumber = item.ParentIndexNumber,
                    PremiereDate = item.PremiereDate,
                    ProductionYear = item.ProductionYear,
                    ProviderIds = item.ProviderIds,
                    SearchProviderName = Name,
                    IndexNumberEnd = item.IndexNumberEnd
                });
            }

            return list;
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private void ParseAdditionalEpisodeXml(string xmlFile, Episode episode, CultureDto[] metadataLanguages)
        {
            var settings = new XmlReaderSettings
            {
                CheckCharacters = false,
                IgnoreProcessingInstructions = true,
                IgnoreComments = true,
                ValidationType = ValidationType.None
            };

            using (var stream = _fileSystem.OpenRead(xmlFile))
            {
                using (var streamReader = new StreamReader(stream))
                using (var reader = XmlReader.Create(streamReader, settings))
                {
                    reader.MoveToContent();

                    var titles = new List<Title>();

                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            switch (reader.Name)
                            {
                                case "length":
                                    var length = reader.ReadElementContentAsString();
                                    if (!string.IsNullOrEmpty(length))
                                    {
                                        long duration;
                                        if (long.TryParse(length, out duration))
                                            episode.RunTimeTicks += TimeSpan.FromMinutes(duration).Ticks;
                                    }

                                    break;

                                case "title":
                                    var language = reader.GetAttribute("xml:lang");
                                    var name = reader.ReadElementContentAsString();

                                    titles.Add(new Title
                                    {
                                        Language = language,
                                        Type = "main",
                                        Name = name
                                    });

                                    break;
                            }
                        }
                    }

                    var title = titles.Localize(metadataLanguages).Name;
                    if (!string.IsNullOrEmpty(title))
                        episode.Name += " / " + title;
                }
            }
        }

        private async Task<string> FindSeriesFolder(string seriesId, CancellationToken cancellationToken)
        {
            var seriesDataPath = await AniDbSeriesProvider.GetSeriesData(_configurationManager.ApplicationPaths, _httpClient, _fileSystem, seriesId, cancellationToken).ConfigureAwait(false);
            return _fileSystem.GetDirectoryName(seriesDataPath);
        }

        private void ParseEpisodeXml(string xmlFile, Episode episode, CultureDto[] metadataLanguages)
        {
            var settings = new XmlReaderSettings
            {
                CheckCharacters = false,
                IgnoreProcessingInstructions = true,
                IgnoreComments = true,
                ValidationType = ValidationType.None
            };

            using (var stream = _fileSystem.OpenRead(xmlFile))
            {
                using (var streamReader = new StreamReader(stream))
                using (var reader = XmlReader.Create(streamReader, settings))
                {
                    reader.MoveToContent();

                    var titles = new List<Title>();

                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            switch (reader.Name)
                            {
                                case "length":
                                    var length = reader.ReadElementContentAsString();
                                    if (!string.IsNullOrEmpty(length))
                                    {
                                        long duration;
                                        if (long.TryParse(length, out duration))
                                            episode.RunTimeTicks = TimeSpan.FromMinutes(duration).Ticks;
                                    }

                                    break;

                                case "airdate":
                                    var airdate = reader.ReadElementContentAsString();
                                    if (!string.IsNullOrEmpty(airdate))
                                    {
                                        DateTime date;
                                        if (DateTime.TryParse(airdate, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out date))
                                        {
                                            episode.PremiereDate = date;
                                            episode.ProductionYear = date.Year;
                                        }
                                    }

                                    break;

                                case "rating":
                                    int count;
                                    float rating;
                                    if (int.TryParse(reader.GetAttribute("votes"), NumberStyles.Any, CultureInfo.InvariantCulture, out count) &&
                                        float.TryParse(reader.ReadElementContentAsString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out rating))
                                    {
                                        episode.CommunityRating = (float)Math.Round(rating, 1);
                                    }

                                    break;

                                case "title":
                                    var language = reader.GetAttribute("xml:lang");
                                    var name = reader.ReadElementContentAsString();

                                    titles.Add(new Title
                                    {
                                        Language = language,
                                        Type = "main",
                                        Name = name
                                    });

                                    break;

                                case "summary":
                                    episode.Overview = AniDbSeriesProvider.ReplaceLineFeedWithNewLine(AniDbSeriesProvider.StripAniDbLinks(reader.ReadElementContentAsString()).Split(new[] { "Source:", "Note:" }, StringSplitOptions.None)[0]);

                                    break;
                            }
                        }
                    }

                    var title = titles.Localize(metadataLanguages).Name;
                    if (!string.IsNullOrEmpty(title))
                        episode.Name = title;
                }
            }
        }

        private string GetEpisodeXmlFile(int? episodeNumber, int? _type, string seriesDataPath)
        {
            if (episodeNumber == null)
            {
                return null;
            }
            string type = _type == 0 ? "S" : "";

            const string nameFormat = "episode-{0}.xml";
            return Path.Combine(seriesDataPath, string.Format(nameFormat, (type ?? "") + episodeNumber.Value));
        }
    }
}