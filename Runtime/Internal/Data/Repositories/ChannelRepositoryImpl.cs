using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Gamania.GIMChat.Internal.Data.Cache;
using Gamania.GIMChat.Internal.Data.DTOs;
using Gamania.GIMChat.Internal.Data.Mappers;
using Gamania.GIMChat.Internal.Data.Network;
using Gamania.GIMChat.Internal.Domain.Commands;
using Gamania.GIMChat.Internal.Domain.Models;
using Gamania.GIMChat.Internal.Domain.Repositories;
using Logger = Gamania.GIMChat.Internal.Domain.Log.Logger;

namespace Gamania.GIMChat.Internal.Data.Repositories
{
    internal class ChannelRepositoryImpl : IChannelRepository
    {
        private const string TAG = "ChannelRepositoryImpl";

        private readonly IHttpClient _httpClient;
        private readonly ConnectionManager _connectionManager;
        private readonly string _baseUrl;
        private readonly ChannelCache _cache;
        private readonly bool _cacheEnabled;

        public ChannelRepositoryImpl(
            IHttpClient httpClient,
            ConnectionManager connectionManager,
            string baseUrl,
            bool enableCache = true,
            ChannelCache cache = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _connectionManager = connectionManager;
            _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
            _cacheEnabled = enableCache;
            _cache = cache ?? new ChannelCache();
        }



        public async Task<BaseChannelBO> GetChannelAsync(
            GimChannelType channelType,
            string channelUrl,
            CancellationToken cancellationToken = default)
        {
            if (channelType == GimChannelType.Group && _cacheEnabled && _cache.TryGet(channelUrl, out var cached))
                return cached;

            return await ExecuteAsync<BaseChannelBO>(async () =>
            {
                var url = $"{_baseUrl}/{channelType.ToPathSegment()}/{Uri.EscapeDataString(channelUrl)}";
                if (channelType == GimChannelType.Group) url += "?member=true";
                var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                return channelType == GimChannelType.Open
                    ? ProcessOpenChannelResponse(response)
                    : ProcessAndCacheGroupChannelResponse(response);
            }, "Failed to get channel", channelUrl);
        }

        public async Task DeleteChannelAsync(
            GimChannelType channelType,
            string channelUrl,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async () =>
            {
                var url = $"{_baseUrl}/{channelType.ToPathSegment()}/{Uri.EscapeDataString(channelUrl)}";
                var response = await _httpClient.DeleteAsync(url, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                if (channelType == GimChannelType.Group && _cacheEnabled)
                    _cache.Remove(channelUrl);
            }, "Failed to delete channel", channelUrl);
        }

        public async Task<RestrictedUserListResult> GetBannedUserListAsync(
            GimChannelType channelType,
            string channelUrl,
            string token,
            int limit,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required", nameof(channelUrl));

            return await ExecuteAsync(async () =>
            {
                var queryParams = new List<string> { $"limit={limit}" };
                if (!string.IsNullOrEmpty(token))
                    queryParams.Add($"token={Uri.EscapeDataString(token)}");

                var url = $"{_baseUrl}/{channelType.ToPathSegment()}/{Uri.EscapeDataString(channelUrl)}/ban?{string.Join("&", queryParams)}";
                var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                return ParseRestrictedUserListResponse(response.Body, "banned_list");
            }, "Failed to get banned user list", channelUrl);
        }

        public async Task<RestrictedUserListResult> GetMutedUserListAsync(
            GimChannelType channelType,
            string channelUrl,
            string token,
            int limit,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required", nameof(channelUrl));

            return await ExecuteAsync(async () =>
            {
                var queryParams = new List<string> { $"limit={limit}" };
                if (!string.IsNullOrEmpty(token))
                    queryParams.Add($"token={Uri.EscapeDataString(token)}");

                var url = $"{_baseUrl}/{channelType.ToPathSegment()}/{Uri.EscapeDataString(channelUrl)}/mute?{string.Join("&", queryParams)}";
                var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                return ParseRestrictedUserListResponse(response.Body, "muted_list");
            }, "Failed to get muted user list", channelUrl);
        }

        public async Task BanUserAsync(
            GimChannelType channelType,
            string channelUrl,
            string userId,
            int seconds,
            string description,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required", nameof(channelUrl));
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("userId is required", nameof(userId));

            await ExecuteAsync(async () =>
            {
                var url = $"{_baseUrl}/{channelType.ToPathSegment()}/{Uri.EscapeDataString(channelUrl)}/ban";
                var requestBody = JsonConvert.SerializeObject(new
                {
                    user_id = userId,
                    seconds = seconds,
                    description = description
                });
                var response = await _httpClient.PostAsync(url, requestBody, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);
            }, "Failed to ban user", channelUrl);
        }

        public async Task UnbanUserAsync(
            GimChannelType channelType,
            string channelUrl,
            string userId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required", nameof(channelUrl));
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("userId is required", nameof(userId));

            await ExecuteAsync(async () =>
            {
                var url = $"{_baseUrl}/{channelType.ToPathSegment()}/{Uri.EscapeDataString(channelUrl)}/ban/{Uri.EscapeDataString(userId)}";
                var response = await _httpClient.DeleteAsync(url, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);
            }, "Failed to unban user", channelUrl);
        }

        public async Task MuteUserAsync(
            GimChannelType channelType,
            string channelUrl,
            string userId,
            int seconds,
            string description,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required", nameof(channelUrl));
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("userId is required", nameof(userId));

            await ExecuteAsync(async () =>
            {
                var url = $"{_baseUrl}/{channelType.ToPathSegment()}/{Uri.EscapeDataString(channelUrl)}/mute";
                var requestBody = JsonConvert.SerializeObject(new
                {
                    user_id = userId,
                    seconds = seconds,
                    description = description
                });
                var response = await _httpClient.PostAsync(url, requestBody, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);
            }, "Failed to mute user", channelUrl);
        }

        public async Task UnmuteUserAsync(
            GimChannelType channelType,
            string channelUrl,
            string userId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required", nameof(channelUrl));
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("userId is required", nameof(userId));

            await ExecuteAsync(async () =>
            {
                var url = $"{_baseUrl}/{channelType.ToPathSegment()}/{Uri.EscapeDataString(channelUrl)}/mute/{Uri.EscapeDataString(userId)}";
                var response = await _httpClient.DeleteAsync(url, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);
            }, "Failed to unmute user", channelUrl);
        }



        public async Task<GroupChannelBO> CreateGroupChannelAsync(
            GimGroupChannelCreateParams createParams,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(async () =>
            {
                var url = $"{_baseUrl}/group_channels";
                var requestBody = JsonConvert.SerializeObject(new
                {
                    user_ids = createParams.UserIds,
                    operator_ids = createParams.OperatorUserIds,
                    name = createParams.Name,
                    cover_url = createParams.CoverUrl,
                    custom_type = createParams.CustomType,
                    data = createParams.Data,
                    is_distinct = createParams.IsDistinct
                });

                var response = await _httpClient.PostAsync(url, requestBody, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                return ProcessAndCacheGroupChannelResponse(response);
            }, "Failed to create group channel");
        }

        public async Task<GroupChannelBO> UpdateGroupChannelAsync(
            string channelUrl,
            GimGroupChannelUpdateParams updateParams,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(async () =>
            {
                var url = $"{_baseUrl}/group_channels/{Uri.EscapeDataString(channelUrl)}";
                var requestBody = JsonConvert.SerializeObject(new
                {
                    name = updateParams.Name,
                    cover_url = updateParams.CoverUrl,
                    custom_type = updateParams.CustomType,
                    data = updateParams.Data
                });

                var response = await _httpClient.PutAsync(url, requestBody, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                return ProcessAndCacheGroupChannelResponse(response);
            }, "Failed to update group channel", channelUrl);
        }

        public async Task<GroupChannelBO> InviteUsersAsync(
            string channelUrl,
            string[] userIds,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(async () =>
            {
                var url = $"{_baseUrl}/group_channels/{Uri.EscapeDataString(channelUrl)}/invite";
                var requestBody = JsonConvert.SerializeObject(new { user_ids = userIds });

                var response = await _httpClient.PostAsync(url, requestBody, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                return ProcessAndCacheGroupChannelResponse(response);
            }, "Failed to invite users", channelUrl);
        }

        public async Task<ChannelListResult> ListGroupChannelsAsync(
            string userId,
            int limit = 20,
            string token = null,
            IList<string> customTypesFilter = null,
            string customTypeStartsWithFilter = null,
            bool includeEmpty = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("userId is required for ListGroupChannels", nameof(userId));

            return await ExecuteAsync(async () =>
            {
                var queryParams = new List<string> { $"limit={limit}" };
                if (!string.IsNullOrEmpty(token))
                    queryParams.Add($"token={Uri.EscapeDataString(token)}");
                if (customTypesFilter != null && customTypesFilter.Count > 0)
                    queryParams.Add($"custom_types={string.Join(",", customTypesFilter.Select(Uri.EscapeDataString))}");
                if (!string.IsNullOrEmpty(customTypeStartsWithFilter))
                    queryParams.Add($"custom_type_startswith={Uri.EscapeDataString(customTypeStartsWithFilter)}");
                if (includeEmpty)
                    queryParams.Add("show_empty=true");
                var url = $"{_baseUrl}/users/{Uri.EscapeDataString(userId)}/my_group_channels?{string.Join("&", queryParams)}";

                var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                var dto = JsonConvert.DeserializeObject<ChannelListResponseDTO>(response.Body);
                var channels = (dto?.channels ?? new List<ChannelDTO>())
                    .Select(ChannelDtoMapper.ToBusinessObject)
                    .Where(c => c != null)
                    .ToList();

                if (_cacheEnabled)
                {
                    foreach (var ch in channels)
                    {
                        if (!string.IsNullOrWhiteSpace(ch.ChannelUrl))
                            _cache.Set(ch.ChannelUrl, ch);
                    }
                }

                return new ChannelListResult
                {
                    Channels = channels.AsReadOnly(),
                    NextToken = string.IsNullOrEmpty(dto?.next) ? null : dto.next
                };
            }, "Failed to list group channels", userId);
        }

        public async Task<ChannelChangeLogResult> GetChangeLogsAsync(
            string userId,
            long syncTimestamp,
            string token = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("userId is required for GetChangeLogs", nameof(userId));

            return await ExecuteAsync(async () =>
            {
                var path = $"{_baseUrl}/users/{userId}/my_group_channels/changelogs";
                var query = $"?change_ts={syncTimestamp}";
                if (!string.IsNullOrEmpty(token))
                    query += $"&token={Uri.EscapeDataString(token)}";
                query += "&show_member=true&show_read_receipt=true&show_delivery_receipt=true";
                var url = path + query;

                var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                var dto = JsonConvert.DeserializeObject<ChannelChangeLogResponseDTO>(response.Body);

                var updatedChannels = (dto?.updated ?? new List<ChannelDTO>())
                    .Select(ChannelDtoMapper.ToBusinessObject)
                    .Where(c => c != null)
                    .ToList();

                if (_cacheEnabled)
                {
                    foreach (var ch in updatedChannels)
                    {
                        if (!string.IsNullOrWhiteSpace(ch.ChannelUrl))
                            _cache.Set(ch.ChannelUrl, ch);
                    }
                }

                var deletedUrls = dto?.deleted ?? new List<string>();

                return new ChannelChangeLogResult
                {
                    UpdatedChannels = updatedChannels.AsReadOnly(),
                    DeletedChannelUrls = deletedUrls.AsReadOnly(),
                    NextToken = string.IsNullOrEmpty(dto?.next) ? null : dto.next
                };
            }, "Failed to get change logs", userId);
        }



        public async Task<OpenChannelBO> CreateOpenChannelAsync(
            GimOpenChannelCreateParams createParams,
            CancellationToken cancellationToken = default)
        {
            if (createParams == null)
                throw new ArgumentNullException(nameof(createParams));

            return await ExecuteAsync(async () =>
            {
                var url = $"{_baseUrl}/open_channels";
                var requestBody = JsonConvert.SerializeObject(new
                {
                    name = createParams.Name,
                    channel_url = createParams.ChannelUrl,
                    cover_url = createParams.CoverUrl,
                    custom_type = createParams.CustomType,
                    data = createParams.Data,
                    operator_ids = createParams.OperatorUserIds
                }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                var response = await _httpClient.PostAsync(url, requestBody, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                return ProcessOpenChannelResponse(response);
            }, "Failed to create open channel");
        }

        public async Task<OpenChannelBO> UpdateOpenChannelAsync(
            string channelUrl,
            GimOpenChannelUpdateParams updateParams,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required", nameof(channelUrl));
            if (updateParams == null)
                throw new ArgumentNullException(nameof(updateParams));

            return await ExecuteAsync(async () =>
            {
                var url = $"{_baseUrl}/open_channels/{Uri.EscapeDataString(channelUrl)}";
                var requestBody = JsonConvert.SerializeObject(new
                {
                    name = updateParams.Name,
                    cover_url = updateParams.CoverUrl,
                    custom_type = updateParams.CustomType,
                    data = updateParams.Data,
                    operator_ids = updateParams.OperatorUserIds
                }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                var response = await _httpClient.PutAsync(url, requestBody, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                return ProcessOpenChannelResponse(response);
            }, "Failed to update open channel", channelUrl);
        }

        public async Task<OpenChannelListResult> ListOpenChannelsAsync(
            int limit = 20,
            string token = null,
            string nameKeyword = null,
            string urlKeyword = null,
            string customType = null,
            bool includeFrozen = true,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(async () =>
            {
                var queryParams = new List<string> { $"limit={limit}" };
                if (!string.IsNullOrEmpty(token))
                    queryParams.Add($"token={Uri.EscapeDataString(token)}");
                if (!string.IsNullOrEmpty(nameKeyword))
                    queryParams.Add($"name_contains={Uri.EscapeDataString(nameKeyword)}");
                if (!string.IsNullOrEmpty(urlKeyword))
                    queryParams.Add($"url_contains={Uri.EscapeDataString(urlKeyword)}");
                if (!string.IsNullOrEmpty(customType))
                    queryParams.Add($"custom_type={Uri.EscapeDataString(customType)}");
                if (!includeFrozen)
                    queryParams.Add("show_frozen=false");

                var url = $"{_baseUrl}/open_channels?{string.Join("&", queryParams)}";
                var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                var dto = JsonConvert.DeserializeObject<OpenChannelListResponseDTO>(response.Body);
                var channels = (dto?.channels ?? new List<OpenChannelDTO>())
                    .Select(OpenChannelDtoMapper.ToBusinessObject)
                    .Where(c => c != null)
                    .ToList();

                return new OpenChannelListResult
                {
                    Channels = channels.AsReadOnly(),
                    NextToken = string.IsNullOrEmpty(dto?.next) ? null : dto.next
                };
            }, "Failed to list open channels");
        }

        public Task<int> EnterChannelAsync(
            string channelUrl,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required", nameof(channelUrl));

            return SendOpenChannelCommandAsync(CommandType.ENTR, channelUrl, cancellationToken);
        }

        public Task<int> ExitChannelAsync(
            string channelUrl,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required", nameof(channelUrl));

            return SendOpenChannelCommandAsync(CommandType.EXIT, channelUrl, cancellationToken);
        }

        private async Task<int> SendOpenChannelCommandAsync(
            CommandType commandType,
            string channelUrl,
            CancellationToken cancellationToken)
        {
            if (_connectionManager == null || !_connectionManager.IsConnected)
                throw new GimException(GimErrorCode.WebSocketConnectionClosed, "Not connected to server");

            var payload = new { channel_url = channelUrl };
            var ackPayload = await _connectionManager.SendCommandAsync(
                commandType, payload, cancellationToken: cancellationToken);

            if (!string.IsNullOrEmpty(ackPayload))
            {
                try
                {
                    var ack = JsonConvert.DeserializeObject<OpenChannelEnterExitAckDTO>(ackPayload);
                    return ack?.participant_count ?? 0;
                }
                catch (Exception e)
                {
                    Logger.Warning(TAG, $"Failed to parse {commandType} ACK: {ackPayload}, error: {e.Message}");
                    return 0;
                }
            }

            return 0;
        }

        public async Task<ParticipantListResult> GetParticipantListAsync(
            string channelUrl,
            string token = null,
            int limit = 20,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentException("channelUrl is required", nameof(channelUrl));

            return await ExecuteAsync(async () =>
            {
                var queryParams = new List<string> { $"limit={limit}" };
                if (!string.IsNullOrEmpty(token))
                    queryParams.Add($"token={Uri.EscapeDataString(token)}");

                var url = $"{_baseUrl}/open_channels/{Uri.EscapeDataString(channelUrl)}/participants?{string.Join("&", queryParams)}";
                var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);
                if (!response.IsSuccess) throw GimException.FromHttpResponse(response);

                var dto = JsonConvert.DeserializeObject<ParticipantListResponseDTO>(response.Body);
                var users = (dto?.participants ?? new List<UserDTO>())
                    .Select(u => new GimUser
                    {
                        UserId = u.user_id,
                        Nickname = u.nickname,
                        ProfileUrl = u.profile_url
                    })
                    .ToList();

                return new ParticipantListResult
                {
                    Users = users.AsReadOnly(),
                    NextToken = string.IsNullOrEmpty(dto?.next) ? null : dto.next
                };
            }, "Failed to get participant list", channelUrl);
        }



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

        private async Task ExecuteAsync(
            Func<Task> operation,
            string errorMessage,
            string context = null)
        {
            try
            {
                await operation();
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

        private RestrictedUserListResult ParseRestrictedUserListResponse(string body, string listKey)
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(body);
            var result = new RestrictedUserListResult
            {
                Users = new List<RestrictedUserBO>(),
                NextToken = json["next"]?.ToString()
            };

            var usersArray = json[listKey] as Newtonsoft.Json.Linq.JArray;
            if (usersArray != null)
            {
                foreach (var userToken in usersArray)
                {
                    var userId = userToken["user_id"]?.ToString();
                    if (string.IsNullOrEmpty(userId)) continue;

                    var endAt = userToken["end_at"]?.ToObject<long?>()
                        ?? userToken["muted_end_at"]?.ToObject<long?>()
                        ?? -1L;

                    result.Users.Add(new RestrictedUserBO
                    {
                        UserId = userId,
                        Nickname = userToken["nickname"]?.ToString(),
                        ProfileUrl = userToken["profile_url"]?.ToString(),
                        Description = userToken["description"]?.ToString()
                            ?? userToken["muted_description"]?.ToString()
                            ?? string.Empty,
                        EndAt = endAt
                    });
                }
            }

            return result;
        }

        private OpenChannelBO ProcessOpenChannelResponse(HttpResponse response)
        {
            var dto = JsonConvert.DeserializeObject<OpenChannelDTO>(response.Body);
            return OpenChannelDtoMapper.ToBusinessObject(dto);
        }

        private GroupChannelBO ProcessGroupChannelResponse(HttpResponse response)
        {
            var channelDto = JsonConvert.DeserializeObject<ChannelDTO>(response.Body);
            return ChannelDtoMapper.ToBusinessObject(channelDto);
        }

        private GroupChannelBO ProcessAndCacheGroupChannelResponse(HttpResponse response)
        {
            var channel = ProcessGroupChannelResponse(response);

            if (_cacheEnabled && !string.IsNullOrWhiteSpace(channel.ChannelUrl))
                _cache.Set(channel.ChannelUrl, channel);

            return channel;
        }
    }
}
