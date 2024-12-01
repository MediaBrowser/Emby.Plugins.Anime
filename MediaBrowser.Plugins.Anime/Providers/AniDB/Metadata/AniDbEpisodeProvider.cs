using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MediaBrowser.Controller.Entities;

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

            if (!info.HasProviderId(ProviderNames.AniDb) && !info.IndexNumber.HasValue)
            {
                return result;
            }

            using (var stream = await AniDbSeriesProvider.GetSeriesDataFile(_configurationManager.ApplicationPaths, _httpClient, _fileSystem, anidbId, cancellationToken).ConfigureAwait(false))
            {
                if (stream != null)
                {
                    var metadataLanguages = info.MetadataLanguages;

                    result.Item = ParseSeriesXml(stream, info, metadataLanguages);
                    result.HasMetadata = result.Item != null;
                }

                return result;
            }
        }

        public string Name => "AniDB";

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var list = new List<RemoteSearchResult>();

            var metadataResult = await GetMetadata(searchInfo, cancellationToken).ConfigureAwait(false);

            if (metadataResult.HasMetadata)
            {
                list.Add(metadataResult.ToRemoteSearchResult(Name));
            }

            return list;
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private Episode ParseSeriesXml(Stream stream, EpisodeInfo info, CultureDto[] metadataLanguages)
        {
            var settings = new XmlReaderSettings
            {
                CheckCharacters = false,
                IgnoreProcessingInstructions = true,
                IgnoreComments = true,
                ValidationType = ValidationType.None
            };

            using (var streamReader = new StreamReader(stream))
            using (var reader = XmlReader.Create(streamReader, settings))
            {
                reader.MoveToContent();
                reader.Read();

                var titles = new List<Title>();

                while (!reader.EOF && reader.ReadState == ReadState.Interactive)
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        switch (reader.Name)
                        {
                            case "episodes":
                                using (var subtree = reader.ReadSubtree())
                                {
                                    return ParseEpisodesNode(subtree, info, metadataLanguages);
                                }

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

            return null;
        }

        private Episode ParseEpisodesNode(XmlReader reader, EpisodeInfo info, CultureDto[] metadataLanguages)
        {
            reader.MoveToContent();
            reader.Read();

            var titles = new List<Title>();

            var id = info.GetProviderId(ProviderNames.AniDb);
            var filterOnNumber = string.IsNullOrEmpty(id);

            BaseItem.Logger.Info("Searching for episode with anidb id: {0}, IndexNumber: {1}", id, info.IndexNumber);

            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "episode":
                            var currentId = reader.GetAttribute("id");

                            if (!string.IsNullOrEmpty(id))
                            {
                                if (!string.Equals(id, currentId, StringComparison.OrdinalIgnoreCase))
                                {
                                    reader.Skip();
                                    break;
                                }
                            }

                            using (var subtree = reader.ReadSubtree())
                            {
                                var episode = ParseEpisodeNode(subtree, info, currentId, filterOnNumber, metadataLanguages);

                                if (episode != null)
                                {
                                    return episode;
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

            return null;
        }


        private Episode ParseEpisodeNode(XmlReader reader, EpisodeInfo info, string nodeId, bool filterOnNumber, CultureDto[] metadataLanguages)
        {
            var episode = new Episode()
            {
                ParentIndexNumber = info.ParentIndexNumber,
                Name = info.Name,
                ProviderIds = new ProviderIdDictionary(info.ProviderIds)
            };

            episode.SetProviderId(ProviderNames.AniDb, nodeId);

            reader.MoveToContent();
            reader.Read();

            var titles = new List<Title>();

            while (!reader.EOF && reader.ReadState == ReadState.Interactive)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "epno":
                            var num = reader.ReadElementContentAsString();

                            if (int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var episodeNumber))
                            {
                                episode.IndexNumber = episodeNumber;
                            }

                            break;

                        case "length":
                            var length = reader.ReadElementContentAsString();
                            if (!string.IsNullOrEmpty(length))
                            {
                                long duration;
                                if (long.TryParse(length, NumberStyles.Integer, CultureInfo.InvariantCulture, out duration))
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

            var title = titles.Localize(metadataLanguages).Name;
            if (!string.IsNullOrEmpty(title))
                episode.Name = title;

            BaseItem.Logger.Info("Found episode with anidb id: {0}, IndexNumber: {1}", episode.GetProviderId(ProviderNames.AniDb), episode.IndexNumber);

            if (filterOnNumber && info.IndexNumber.HasValue)
            {
                if (episode.IndexNumber != info.IndexNumber)
                {
                    return null;
                }
            }

            return episode;
        }
    }
}