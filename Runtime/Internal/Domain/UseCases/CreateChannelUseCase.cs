using System;
using System.Threading;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Domain.Repositories;

namespace Gamania.GIMChat.Internal.Domain.UseCases
{
    /// <summary>
    /// Handles the creation of a new group channel, including input validation and repository interaction.
    /// </summary>
    internal class CreateChannelUseCase
    {
        private readonly IChannelRepository _channelRepository;

        public CreateChannelUseCase(IChannelRepository channelRepository)
        {
            _channelRepository = channelRepository ?? throw new ArgumentNullException(nameof(channelRepository));
        }

        /// <summary>
        /// Asynchronously creates a new group channel based on the provided parameters.
        /// </summary>
        /// <param name="createParams">Channel creation parameters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Created GimGroupChannel object</returns>
        /// <exception cref="GimException">If validation fails or creation fails</exception>
        public async Task<GimGroupChannel> ExecuteAsync(
            GimGroupChannelCreateParams createParams,
            CancellationToken cancellationToken = default)
        {
            // Validation
            if (createParams == null)
            {
                throw new GimException(
                    GimErrorCode.InvalidParameter,
                    "Channel creation parameters cannot be null",
                    "createParams");
            }

            if (createParams.UserIds == null || createParams.UserIds.Count == 0)
            {
                throw new GimException(
                    GimErrorCode.InvalidParameter,
                    "UserIds cannot be null or empty",
                    "createParams.UserIds");
            }

            // Execute repository call
            try
            {
                var channelBo = await _channelRepository.CreateGroupChannelAsync(createParams, cancellationToken)
                ?? throw new GimException(
                        GimErrorCode.UnknownError,
                        "Failed to create channel - repository returned null");

                // Convert BO to Public Model
                return GroupChannelBoMapper.ToPublicModel(channelBo);
            }
            catch (GimException)
            {
                // Re-throw GimException as is
                throw;
            }
            catch (Exception ex)
            {
                // Wrap other exceptions in GimException
                throw new GimException(
                    GimErrorCode.UnknownError,
                    "Failed to create channel",
                    ex);
            }
        }
    }
}
