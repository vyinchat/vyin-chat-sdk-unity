using System.Collections.Generic;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Log;
using Gamania.GIMChat.Internal.Platform.Unity;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Query object for listing participants in an open channel with pagination.
    /// </summary>
    public class GimParticipantListQuery
    {
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

        /// <summary>
        /// Max number of participants per page.
        /// </summary>
        public int Limit { get; }

        /// <summary>
        /// The channel URL this query targets.
        /// </summary>
        public string ChannelUrl { get; }

        internal GimParticipantListQuery(string channelUrl, int limit = 20)
        {
            ChannelUrl = channelUrl;
            Limit = limit;
        }

        /// <summary>
        /// Loads the next page of participants.
        /// </summary>
        public async Task<IReadOnlyList<GimUser>> LoadNextAsync()
        {
            if (!HasNext)
            {
                Logger.Info(LogCategory.Channel, "No more participants to load.");
                return new List<GimUser>().AsReadOnly();
            }

            if (_isLoading)
            {
                Logger.Info(LogCategory.Channel, "Participant list load already in progress.");
                return new List<GimUser>().AsReadOnly();
            }

            _isLoading = true;
            try
            {
                var repository = GIMChatMain.Instance?.GetChannelRepository()
                    ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

                var result = await repository.GetParticipantListAsync(
                    channelUrl: ChannelUrl,
                    token: _nextToken,
                    limit: Limit
                );

                _nextToken = result.NextToken;
                HasNext = !string.IsNullOrEmpty(result.NextToken);

                return result.Users;
            }
            finally
            {
                _isLoading = false;
            }
        }
    }
}
