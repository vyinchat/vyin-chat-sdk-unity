using System;
using System.Threading;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Domain.Models;
using Gamania.GIMChat.Internal.Domain.Repositories;

namespace Gamania.GIMChat.Internal.Domain.UseCases
{
    internal class GetGroupChannelUseCase
    {
        private readonly IChannelRepository _channelRepository;

        public GetGroupChannelUseCase(IChannelRepository channelRepository)
        {
            _channelRepository = channelRepository ?? throw new ArgumentNullException(nameof(channelRepository));
        }

        /// <summary>
        /// Retrieves a channel by its URL
        /// </summary>
        /// <param name="channelUrl">The URL of the channel to retrieve</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The channel information</returns>
        /// <exception cref="GimException">Thrown when the channel URL is invalid or the channel is not found</exception>
        public async Task<GimGroupChannel> ExecuteAsync(
            string channelUrl,
            CancellationToken cancellationToken = default)
        {
            // Validation
            if (string.IsNullOrWhiteSpace(channelUrl))
            {
                throw new GimException(
                    GimErrorCode.InvalidParameter,
                    "Channel URL cannot be null or empty",
                    "channelUrl");
            }

            try
            {
                var bo = await _channelRepository.GetChannelAsync(GimChannelType.Group, channelUrl, cancellationToken);

                if (bo == null)
                {
                    throw new GimException(
                        GimErrorCode.ErrChannelNotFound,
                        $"Channel not found: {channelUrl}",
                        channelUrl);
                }

                return GroupChannelBoMapper.ToPublicModel((GroupChannelBO)bo);
            }
            catch (GimException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GimException(
                    GimErrorCode.UnknownError,
                    "Failed to get channel",
                    channelUrl,
                    ex);
            }
        }
    }
}
