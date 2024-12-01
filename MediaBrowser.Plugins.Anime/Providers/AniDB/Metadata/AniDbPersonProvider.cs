using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Plugins.Anime.Providers.AniDB.Metadata
{
    public class AniDbPersonProvider : IRemoteMetadataProvider<Person, PersonLookupInfo>
    {
        private readonly IApplicationPaths _paths;
        private readonly IFileSystem _fileSystem;

        public AniDbPersonProvider(IApplicationPaths paths, IFileSystem fileSystem)
        {
            _paths = paths;
            _fileSystem = fileSystem;
        }

        public Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Person>();

            if (!string.IsNullOrEmpty(info.GetProviderId(ProviderNames.AniDb)))
                return Task.FromResult(result);

            var person = AniDbSeriesProvider.GetPersonInfo(_paths.CachePath, info.Name, _fileSystem);
            if (!string.IsNullOrEmpty(person?.Id))
            {
                result.Item = new Person();
                result.HasMetadata = true;

                result.Item.SetProviderId(ProviderNames.AniDb, person.Id);
            }

            return Task.FromResult(result);
        }

        public string Name => "AniDB";

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult(Enumerable.Empty<RemoteSearchResult>());
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }

    public class AniDbPersonImageProvider : IRemoteImageProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly IApplicationPaths _paths;
        private readonly IFileSystem _fileSystem;

        public AniDbPersonImageProvider(IApplicationPaths paths, IHttpClient httpClient, IFileSystem fileSystem)
        {
            _paths = paths;
            _httpClient = httpClient;
            _fileSystem = fileSystem;   
        }

        public bool Supports(BaseItem item)
        {
            return item is Person;
        }

        public string Name => "AniDB";

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions, CancellationToken cancellationToken)
        {
            var infos = new List<RemoteImageInfo>();

            var person = AniDbSeriesProvider.GetPersonInfo(_paths.CachePath, item.Name, _fileSystem);
            if (person != null)
            {
                infos.Add(new RemoteImageInfo
                {
                    Url = person.Image,
                    Type = ImageType.Primary,
                    ProviderName = Name
                });
            }

            return Task.FromResult<IEnumerable<RemoteImageInfo>>(infos);
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
    }
}