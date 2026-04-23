using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Gamania.GIMChat.Internal.Data.DTOs;
using Gamania.GIMChat.Internal.Data.Mappers;
using Gamania.GIMChat.Internal.Data.Network;
using Gamania.GIMChat.Internal.Domain.Commands;
using Gamania.GIMChat.Internal.Domain.Log;
using Gamania.GIMChat.Internal.Domain.Message;
using Gamania.GIMChat.Internal.Domain.Models;
using Gamania.GIMChat.Internal.Domain.Repositories;

namespace Gamania.GIMChat.Internal.Data.Repositories
{
    /// <summary>
    /// Message data layer implementation, responsible for sending messages (WebSocket) and fetching historical messages/logs (HTTP).
    /// </summary>
    internal class MessageRepositoryImpl : IMessageRepository
    {
        private readonly ConnectionManager _connectionManager;
        private readonly IHttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly TimeSpan _ackTimeout = TimeSpan.FromSeconds(15);

        public MessageRepositoryImpl(ConnectionManager connectionManager, IHttpClient httpClient, string baseUrl)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        }

        /// <summary>
        /// Sends MESG command via WebSocket to publish a message.
        /// </summary>
        public async Task<MessageBO> SendMessageAsync(
            string channelUrl,
            GimUserMessageCreateParams createParams,
            CancellationToken cancellationToken = default)
        {
            if (!_connectionManager.IsConnected || string.IsNullOrEmpty(_connectionManager.SessionKey))
                throw new GimException(GimErrorCode.ConnectionRequired, "Cannot send message: Not connected.");

            try
            {
                var payload = new
                {
                    channel_url = channelUrl,
                    message = createParams.Message,
                    message_type = "MESG",
                    data = createParams.Data ?? "",
                    custom_type = createParams.CustomType ?? ""
                };

                Logger.Debug(LogCategory.Command, $"Sending MESG command for channel: {channelUrl}");

                string ackPayload = await _connectionManager.SendCommandAsync(
                    CommandType.MESG,
                    payload,
                    _ackTimeout,
                    cancellationToken
                );

                if (string.IsNullOrEmpty(ackPayload))
                {
                    throw new GimException(GimErrorCode.AckTimeout, "Message send timeout after 15 seconds");
                }

                Logger.Debug(LogCategory.Command, $"MESG ACK received: {ackPayload}");
                return ParseMessageFromAck(ackPayload, channelUrl, createParams.Message);
            }
            catch (TaskCanceledException)
            {
                throw new GimException(GimErrorCode.AckTimeout, "Message send timeout after 15 seconds");
            }
            catch (GimException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GimException(GimErrorCode.UnknownError, $"SendMessage error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Fetches historical messages for specified channel via HTTP GET.
        /// Supports bidirectional pagination with count limits.
        /// </summary>
        public async Task<IReadOnlyList<MessageBO>> GetMessagesAsync(
            string channelUrl,
            long messageTs,
            int prevLimit = 20,
            int nextLimit = 20,
            bool reverse = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required for GetMessages", nameof(channelUrl));

            return await ExecuteAsync(async () =>
            {
                var path = $"{_baseUrl}/group_channels/{channelUrl}/messages";
                var query = $"?message_ts={messageTs}&prev_limit={prevLimit}&next_limit={nextLimit}&reverse={reverse.ToString().ToLowerInvariant()}";
                var url = path + query;

                var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);

                if (!response.IsSuccess)
                {
                    throw CreateExceptionFromResponse(response);
                }

                var dto = JsonConvert.DeserializeObject<MessageListResponseDTO>(response.Body);
                var messages = (dto?.messages ?? new List<MessageDTO>())
                    .Select(MessageDtoMapper.ToBusinessObject)
                    .Where(m => m != null)
                    .ToList();

                return messages.AsReadOnly();
            }, "Failed to get messages", channelUrl);
        }

        /// <summary>
        /// Gets historical messages for specified channel using GimMessageListParams (pagination).
        /// Supports advanced filtering (message type, custom type, sender, etc.).
        /// </summary>
        public async Task<GetMessagesResult> GetMessagesAsync(
            string channelUrl,
            long messageTs,
            GimMessageListParams messageListParams,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required for GetMessages", nameof(channelUrl));

            messageListParams ??= new GimMessageListParams();

            Logger.Debug(LogCategory.Message, $"GetMessages: ts={messageTs}, prev={messageListParams.PreviousResultSize}, next={messageListParams.NextResultSize}");

            return await ExecuteAsync(async () =>
            {
                var path = $"{_baseUrl}/group_channels/{channelUrl}/messages";
                var query = BuildMessageListQuery(messageTs, messageListParams);
                var url = path + query;

                var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);

                if (!response.IsSuccess)
                {
                    throw CreateExceptionFromResponse(response);
                }

                var dto = JsonConvert.DeserializeObject<MessageListResponseDTO>(response.Body);
                var messages = (dto?.messages ?? new List<MessageDTO>())
                    .Select(MessageDtoMapper.ToBusinessObject)
                    .Where(m => m != null)
                    .ToList();

                var result = new GetMessagesResult
                {
                    Messages = messages.AsReadOnly(),
                    HasPrevious = dto?.has_prev ?? (messages.Count >= messageListParams.PreviousResultSize),
                    HasNext = dto?.has_next ?? (messages.Count >= messageListParams.NextResultSize)
                };

                Logger.Debug(LogCategory.Message, $"GetMessages result: count={messages.Count}, HasPrevious={result.HasPrevious}, HasNext={result.HasNext}");

                return result;
            }, "Failed to get messages", channelUrl);
        }

        /// <summary>
        /// Builds query string based on GimMessageListParams.
        /// </summary>
        private string BuildMessageListQuery(long messageTs, GimMessageListParams p)
        {
            // iOS SDK logic: auto-set inclusive when both prev and next > 0
            var isInclusive = (p.PreviousResultSize > 0 && p.NextResultSize > 0) || p.IsInclusive;

            var queryParams = new List<string>
            {
                $"message_ts={messageTs}",
                $"prev_limit={p.PreviousResultSize}",
                $"next_limit={p.NextResultSize}",
                $"include={isInclusive.ToString().ToLowerInvariant()}"
            };

            // Include options (following iOS SDK format)
            queryParams.Add($"include_reactions={p.IncludeReactions.ToString().ToLowerInvariant()}");
            queryParams.Add($"include_thread_info={p.IncludeThreadInfo.ToString().ToLowerInvariant()}");
            queryParams.Add($"include_parent_message_info={p.IncludeParentMessageInfo.ToString().ToLowerInvariant()}");
            queryParams.Add($"include_reply_type={p.ReplyType.ToApiValue()}");

            // Custom type filter
            if (p.CustomTypes != null && p.CustomTypes.Count > 0)
            {
                queryParams.Add($"custom_types={string.Join(",", p.CustomTypes)}");
            }

            return "?" + string.Join("&", queryParams);
        }

        /// <summary>
        /// Fetches message changelogs (updates and deletes) via HTTP GET.
        /// The tokenTimestamp is the reference point from last sync.
        /// </summary>
        public async Task<MessageChangeLogResult> GetMessageChangeLogsAsync(
            string channelUrl,
            long tokenTimestamp,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required for GetMessageChangeLogs", nameof(channelUrl));

            return await ExecuteAsync(async () =>
            {
                var path = $"{_baseUrl}/group_channels/{channelUrl}/messages/changelogs";
                var query = $"?change_ts={tokenTimestamp}";
                var url = path + query;

                var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);

                if (!response.IsSuccess)
                {
                    throw CreateExceptionFromResponse(response);
                }

                var dto = JsonConvert.DeserializeObject<MessageChangeLogResponseDTO>(response.Body);

                var updatedMessages = (dto?.updated ?? new List<MessageDTO>())
                    .Select(MessageDtoMapper.ToBusinessObject)
                    .Where(m => m != null)
                    .ToList();

                var deletedMessageIds = dto?.deleted ?? new List<long>();

                return new MessageChangeLogResult
                {
                    UpdatedMessages = updatedMessages.AsReadOnly(),
                    DeletedMessageIds = deletedMessageIds.AsReadOnly(),
                    HasMore = dto?.has_more ?? false,
                    NextToken = dto?.next
                };
            }, "Failed to get message change logs", channelUrl);
        }

        /// <summary>
        /// Fetches Huge Gap check via HTTP GET.
        /// </summary>
        public async Task<MessageGapCheckResult> CheckMessageGapAsync(
            string channelUrl,
            long prevStartTs,
            long prevEndTs,
            int prevCacheCount,
            long nextStartTs,
            long nextEndTs,
            int nextCacheCount,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required for CheckMessageGap", nameof(channelUrl));

            return await ExecuteAsync(async () =>
            {
                var path = $"{_baseUrl}/group_channels/{channelUrl}/messages_gap";

                var queryParams = new List<string>
                {
                    $"prev_start_ts={prevStartTs}",
                    $"prev_end_ts={prevEndTs}",
                    $"prev_cache_count={prevCacheCount}",
                    $"next_start_ts={nextStartTs}",
                    $"next_end_ts={nextEndTs}",
                    $"next_cache_count={nextCacheCount}"
                };

                var query = "?" + string.Join("&", queryParams);
                var url = path + query;

                var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);

                if (!response.IsSuccess)
                {
                    throw CreateExceptionFromResponse(response);
                }

                var dto = JsonConvert.DeserializeObject<MessageGapCheckResponseDTO>(response.Body);

                var prevMessages = (dto?.prev_messages ?? new List<MessageDTO>())
                    .Select(MessageDtoMapper.ToBusinessObject)
                    .Where(m => m != null)
                    .ToList();

                var nextMessages = (dto?.next_messages ?? new List<MessageDTO>())
                    .Select(MessageDtoMapper.ToBusinessObject)
                    .Where(m => m != null)
                    .ToList();

                return new MessageGapCheckResult
                {
                    IsHugeGap = dto?.is_huge_gap ?? false,
                    PrevMessages = prevMessages.AsReadOnly(),
                    PrevHasMore = dto?.prev_has_more ?? false,
                    NextMessages = nextMessages.AsReadOnly(),
                    NextHasMore = dto?.next_has_more ?? false
                };
            }, "Failed to check message gap", channelUrl);
        }

        /// <summary>
        /// Generic async execution and error handling wrapper.
        /// </summary>
        private async Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation,
            string errorMessage,
            string context = null)
        {
            try
            {
                return await operation();
            }
            catch (GimException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GimException(GimErrorCode.NetworkError, errorMessage, context, ex);
            }
        }

        private GimException CreateExceptionFromResponse(HttpResponse response)
        {
            return GimException.FromHttpResponse(response);
        }

        /// <summary>
        /// Parses server-returned WebSocket ACK JSON.
        /// If parsing fails, falls back to creating a virtual MessageBO based on local params to prevent crashes.
        /// </summary>
        private MessageBO ParseMessageFromAck(string ackPayload, string channelUrl, string messageText)
        {
            try
            {
                var dto = JsonConvert.DeserializeObject<MessageDTO>(ackPayload);

                if (dto == null)
                {
                    Logger.Warning(LogCategory.Message, "Failed to deserialize ACK payload, using fallback");
                    return CreateFallbackMessage(channelUrl, messageText);
                }

                // Handle fallback values (business logic)
                if (string.IsNullOrEmpty(dto.channel_url))
                    dto.channel_url = channelUrl;
                if (string.IsNullOrEmpty(dto.message))
                    dto.message = messageText;

                return MessageDtoMapper.ToBusinessObject(dto);
            }
            catch (Exception ex)
            {
                Logger.Warning(LogCategory.Message, $"Failed to parse ACK payload: {ex.Message}. Using fallback.");
                return CreateFallbackMessage(channelUrl, messageText);
            }
        }

        /// <summary>
        /// Creates a fallback MessageBO.
        /// </summary>
        private MessageBO CreateFallbackMessage(string channelUrl, string messageText)
        {
            return new MessageBO
            {
                ChannelUrl = channelUrl,
                Message = messageText,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
    }
}
