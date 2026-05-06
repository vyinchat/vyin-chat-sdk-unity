using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Message;
using Gamania.GIMChat.Internal.Domain.Models;

namespace Gamania.GIMChat.Internal.Domain.Repositories
{
    /// <summary>
    /// Repository interface responsible for message-related network requests.
    /// Defines functionality for sending messages, fetching historical messages, and syncing changelogs.
    /// </summary>
    internal interface IMessageRepository
    {
        /// <summary>
        /// Sends a user message to the specified channel via MESG command.
        /// </summary>
        Task<MessageBO> SendMessageAsync(
            string channelUrl,
            GimUserMessageCreateParams createParams,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a file message to the specified channel via FILE command.
        /// </summary>
        /// <param name="channelUrl">Channel URL.</param>
        /// <param name="objectId">Server-assigned object ID from upload (null if using fileUrl).</param>
        /// <param name="fileUrl">Direct file URL (null if using objectId).</param>
        /// <param name="fileName">File name.</param>
        /// <param name="mimeType">MIME type.</param>
        /// <param name="fileSize">File size in bytes.</param>
        /// <param name="createParams">Original create params for data/customType.</param>
        Task<MessageBO> SendFileMessageAsync(
            string channelUrl,
            string objectId,
            string fileUrl,
            string fileName,
            string mimeType,
            long fileSize,
            GimFileMessageCreateParams createParams,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets historical messages for specified channel using a timestamp as reference (pagination).
        /// </summary>
        /// <param name="channelUrl">Channel URL.</param>
        /// <param name="messageTs">Reference timestamp.</param>
        /// <param name="prevLimit">Number of messages to fetch backward.</param>
        /// <param name="nextLimit">Number of messages to fetch forward.</param>
        /// <param name="reverse">Whether to reverse the returned list order.</param>
        Task<IReadOnlyList<MessageBO>> GetMessagesAsync(
            string channelUrl,
            long messageTs,
            int prevLimit = 20,
            int nextLimit = 20,
            bool reverse = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets historical messages for specified channel using GimMessageListParams (pagination).
        /// Supports advanced filtering (message type, custom type, sender, etc.).
        /// </summary>
        /// <param name="channelUrl">Channel URL.</param>
        /// <param name="messageTs">Reference timestamp.</param>
        /// <param name="messageListParams">Message list parameters.</param>
        Task<GetMessagesResult> GetMessagesAsync(
            string channelUrl,
            long messageTs,
            GimMessageListParams messageListParams,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets message changelogs since the specified timestamp.
        /// </summary>
        /// <param name="channelUrl">Channel URL.</param>
        /// <param name="tokenTimestamp">Timestamp of last sync.</param>
        Task<MessageChangeLogResult> GetMessageChangeLogsAsync(
            string channelUrl,
            long tokenTimestamp,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks message gap (Huge Gap) between local cache and server.
        /// Pass local cache range and count; server will compare if too many are missing.
        /// </summary>
        Task<MessageGapCheckResult> CheckMessageGapAsync(
            string channelUrl,
            long prevStartTs,
            long prevEndTs,
            int prevCacheCount,
            long nextStartTs,
            long nextEndTs,
            int nextCacheCount,
            CancellationToken cancellationToken = default);
    }
}
