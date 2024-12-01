using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.IO;
using System.Globalization;
using System;

namespace MediaBrowser.Plugins.Anime.Providers.AniDB.Metadata
{
    public class AniDbSeriesImagesProvider : IRemoteImageProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly IApplicationPaths _appPaths;
        private readonly IFileSystem _fileSystem;

        public AniDbSeriesImagesProvider(IHttpClient httpClient, IApplicationPaths appPaths, IFileSystem fileSystem)
        {
            _httpClient = httpClient;
            _appPaths = appPaths;
            _fileSystem = fileSystem;
        }

        public async Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            await AniDbSeriesProvider.RequestLimiter.Tick(cancellationToken).ConfigureAwait(false);

            return await _httpClient.GetResponse(new HttpRequestOptions
            {
                CancellationToken = cancellationToken,
                Url = url

            }).ConfigureAwait(false);
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions, CancellationToken cancellationToken)
        {
            var series = (Series)item;
            var seriesId = series.GetProviderId(ProviderNames.AniDb);
            return GetImages(seriesId, cancellationToken);
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(string aniDbId, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();

            if (!string.IsNullOrEmpty(aniDbId))
            {
                using (var stream = await AniDbSeriesProvider.GetSeriesDataFile(_appPaths, _httpClient, _fileSystem, aniDbId, cancellationToken).ConfigureAwait(false))
                {
                    if (stream != null)
                    {
                        var imageUrl = FindImageUrl(stream);

                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            list.Add(new RemoteImageInfo
                            {
                                ProviderName = Name,
                                Url = imageUrl
                            });
                        }
                    }
                }
            }

            return list;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary };
        }

        public string Name => "AniDB";

        public bool Supports(BaseItem item)
        {
            return item is Series;
        }

        private string FindImageUrl(Stream seriesData)
        {
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
                            case "picture":
                                return "http://img7.anidb.net/pics/anime/" + reader.ReadElementContentAsString();

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
    }
}