using System;
using System.Threading;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Domain.Models;
using Gamania.GIMChat.Internal.Domain.Repositories;

namespace Gamania.GIMChat.Internal.Domain.UseCases
{
    internal class GetOpenChannelUseCase
    {
        private readonly IChannelRepository _channelRepository;

        public GetOpenChannelUseCase(IChannelRepository channelRepository)
        {
            _channelRepository = channelRepository ?? throw new ArgumentNullException(nameof(channelRepository));
        }

        public async Task<GimOpenChannel> ExecuteAsync(
            string channelUrl,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(channelUrl))
            {
                throw new GimException(
                    GimErrorCode.InvalidParameter,
                    "Channel URL cannot be null or empty",
                    "channelUrl");
            }

            try
            {
                var bo = await _channelRepository.GetChannelAsync(GimChannelType.Open, channelUrl, cancellationToken);

                if (bo == null)
                {
                    throw new GimException(
                        GimErrorCode.ErrChannelNotFound,
                        $"Channel not found: {channelUrl}",
                        channelUrl);
                }

                return OpenGroupChannelBoMapper.ToPublicModel((OpenChannelBO)bo);
            }
            catch (GimException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GimException(
                    GimErrorCode.UnknownError,
                    "Failed to get open channel",
                    channelUrl,
                    ex);
            }
        }
    }
}
