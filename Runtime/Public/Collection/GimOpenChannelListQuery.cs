using System.Collections.Generic;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Domain.Log;
using Gamania.GIMChat.Internal.Platform.Unity;
using Logger = Gamania.GIMChat.Internal.Domain.Log.Logger;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Query object for listing open channels with pagination.
    /// Aligns with <see cref="GimGroupChannelListQuery"/> (async <c>LoadNextAsync</c> only).
    /// </summary>
    public class GimOpenChannelListQuery
    {
        private readonly GimOpenChannelListQueryParams _params;
        private string _nextToken;
        private bool _isLoading;

        /// <summary>
        /// Whether there are more pages to load.
        /// </summary>
        public bool HasNext { get; private set; } = true;

        /// <summary>
        /// Whether the query is currently loading.
        /// </summary>
        public bool IsLoading => _isLoading;

        internal GimOpenChannelListQuery(GimOpenChannelListQueryParams @params)
        {
            _params = @params ?? new GimOpenChannelListQueryParams();
        }

        /// <summary>
        /// Loads the next page of open channels.
        /// </summary>
        public async Task<IReadOnlyList<GimOpenChannel>> LoadNextAsync()
        {
            if (!HasNext)
            {
                Logger.Info(LogCategory.Channel, "No more open channels to load.");
                return new List<GimOpenChannel>().AsReadOnly();
            }

            if (_isLoading)
            {
                Logger.Info(LogCategory.Channel, "Open channel list load already in progress.");
                return new List<GimOpenChannel>().AsReadOnly();
            }

            _isLoading = true;
            try
            {
                var repository = GIMChatMain.Instance?.GetChannelRepository();
                if (repository == null)
                {
                    throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");
                }

                var result = await repository.ListOpenChannelsAsync(
                    limit: _params.Limit,
                    token: _nextToken,
                    nameKeyword: _params.NameKeyword,
                    urlKeyword: _params.UrlKeyword,
                    customType: _params.CustomTypeFilter,
                    includeFrozen: _params.IncludeFrozen
                );

                _nextToken = result.NextToken;
                HasNext = !string.IsNullOrEmpty(result.NextToken);

                var channels = new List<GimOpenChannel>();
                foreach (var bo in result.Channels)
                {
                    var channel = OpenGroupChannelBoMapper.ToPublicModel(bo);
                    if (channel != null)
                    {
                        channels.Add(channel);
                    }
                }

                return channels.AsReadOnly();
            }
            finally
            {
                _isLoading = false;
            }
        }
    }
}
