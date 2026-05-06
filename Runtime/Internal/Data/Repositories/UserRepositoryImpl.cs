using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Gamania.GIMChat.Internal.Data.Network;
using Gamania.GIMChat.Internal.Domain.Repositories;

namespace Gamania.GIMChat.Internal.Data.Repositories
{
    /// <summary>
    /// Implementation of user repository.
    /// </summary>
    public class UserRepositoryImpl : IUserRepository
    {
        private readonly IHttpClient _httpClient;
        private readonly string _baseUrl;

        public UserRepositoryImpl(IHttpClient httpClient, string baseUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        }

        /// <summary>
        /// Updates user information.
        /// </summary>
        public async Task<UserUpdateResult> UpdateUserInfoAsync(
            string userId,
            string nickname,
            string profileUrl,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new GimException(GimErrorCode.InvalidParameter, "userId cannot be null or empty.");
            }

            var url = $"{_baseUrl}/users/{userId}";

            // Build request body with only non-null fields
            var requestObject = new JObject();

            if (nickname != null)
            {
                requestObject["nickname"] = nickname;
            }

            if (profileUrl != null)
            {
                requestObject["profile_url"] = profileUrl;
            }

            var requestBody = requestObject.ToString(Formatting.None);

            var response = await _httpClient.PutAsync(url, requestBody, cancellationToken: cancellationToken);

            if (!response.IsSuccess)
            {
                throw CreateExceptionFromResponse(response);
            }

            // Parse response
            var json = JObject.Parse(response.Body);
            return new UserUpdateResult
            {
                UserId = json["user_id"]?.ToString(),
                Nickname = json["nickname"]?.ToString(),
                ProfileUrl = json["profile_url"]?.ToString()
            };
        }

        /// <summary>
        /// Gets a paginated list of application users.
        /// </summary>
        public async Task<UserListResult> GetUserListAsync(
            string token,
            int limit,
            string nicknameStartsWith,
            List<string> userIds,
            (string Key, List<string> Values)? metaDataFilter,
            CancellationToken cancellationToken = default)
        {
            var queryParams = new List<string> { $"limit={limit}" };

            if (!string.IsNullOrEmpty(token))
            {
                queryParams.Add($"token={Uri.EscapeDataString(token)}");
            }

            if (!string.IsNullOrEmpty(nicknameStartsWith))
            {
                queryParams.Add($"nickname_startswith={Uri.EscapeDataString(nicknameStartsWith)}");
            }

            if (userIds != null && userIds.Count > 0)
            {
                queryParams.Add($"user_ids={Uri.EscapeDataString(string.Join(",", userIds))}");
            }

            if (metaDataFilter.HasValue && metaDataFilter.Value.Values != null && metaDataFilter.Value.Values.Count > 0)
            {
                var metaKey = metaDataFilter.Value.Key;
                var metaValues = string.Join(",", metaDataFilter.Value.Values);
                queryParams.Add($"metadatakey={Uri.EscapeDataString(metaKey)}");
                queryParams.Add($"metadatavalues={Uri.EscapeDataString(metaValues)}");
            }

            var url = $"{_baseUrl}/users?{string.Join("&", queryParams)}";
            var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);

            if (!response.IsSuccess)
            {
                throw CreateExceptionFromResponse(response);
            }

            var json = JObject.Parse(response.Body);
            var result = new UserListResult
            {
                Users = new List<UserBO>(),
                NextToken = json["next"]?.ToString()
            };

            var usersArray = json["users"] as JArray;
            if (usersArray != null)
            {
                foreach (var userToken in usersArray)
                {
                    result.Users.Add(new UserBO
                    {
                        UserId = userToken["user_id"]?.ToString(),
                        Nickname = userToken["nickname"]?.ToString(),
                        ProfileUrl = userToken["profile_url"]?.ToString()
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a paginated list of users blocked by the specified user.
        /// </summary>
        public async Task<UserListResult> GetBlockedUserListAsync(
            string userId,
            string token,
            int limit,
            List<string> userIds,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new GimException(GimErrorCode.InvalidParameter, "userId cannot be null or empty.");
            }

            var queryParams = new List<string> { $"limit={limit}" };

            if (!string.IsNullOrEmpty(token))
            {
                queryParams.Add($"token={Uri.EscapeDataString(token)}");
            }

            if (userIds != null && userIds.Count > 0)
            {
                queryParams.Add($"user_ids={Uri.EscapeDataString(string.Join(",", userIds))}");
            }

            var url = $"{_baseUrl}/users/{userId}/block?{string.Join("&", queryParams)}";
            var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);

            if (!response.IsSuccess)
            {
                throw CreateExceptionFromResponse(response);
            }

            var json = JObject.Parse(response.Body);
            var result = new UserListResult
            {
                Users = new List<UserBO>(),
                NextToken = json["next"]?.ToString()
            };

            var usersArray = json["users"] as JArray;
            if (usersArray != null)
            {
                foreach (var userToken in usersArray)
                {
                    result.Users.Add(new UserBO
                    {
                        UserId = userToken["user_id"]?.ToString(),
                        Nickname = userToken["nickname"]?.ToString(),
                        ProfileUrl = userToken["profile_url"]?.ToString()
                    });
                }
            }

            return result;
        }

        public async Task<UserBO> BlockUserAsync(
            string currentUserId,
            string targetUserId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(currentUserId))
                throw new GimException(GimErrorCode.InvalidParameter, "currentUserId cannot be null or empty.");
            if (string.IsNullOrEmpty(targetUserId))
                throw new GimException(GimErrorCode.InvalidParameter, "targetUserId cannot be null or empty.");

            var url = $"{_baseUrl}/users/{currentUserId}/block";
            var requestBody = JsonConvert.SerializeObject(new { target_id = targetUserId });
            var response = await _httpClient.PostAsync(url, requestBody, cancellationToken: cancellationToken);

            if (!response.IsSuccess)
                throw CreateExceptionFromResponse(response);

            var json = JObject.Parse(response.Body);
            return new UserBO
            {
                UserId = json["user_id"]?.ToString(),
                Nickname = json["nickname"]?.ToString(),
                ProfileUrl = json["profile_url"]?.ToString()
            };
        }

        public async Task UnblockUserAsync(
            string currentUserId,
            string targetUserId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(currentUserId))
                throw new GimException(GimErrorCode.InvalidParameter, "currentUserId cannot be null or empty.");
            if (string.IsNullOrEmpty(targetUserId))
                throw new GimException(GimErrorCode.InvalidParameter, "targetUserId cannot be null or empty.");

            var url = $"{_baseUrl}/users/{currentUserId}/block/{targetUserId}";
            var response = await _httpClient.DeleteAsync(url, cancellationToken: cancellationToken);

            if (!response.IsSuccess)
                throw CreateExceptionFromResponse(response);
        }

        private static GimException CreateExceptionFromResponse(HttpResponse response)
        {
            var errorCode = GimErrorCode.UnknownError;
            var message = response.Error ?? "Unknown error";

            // Try to parse error from response body
            if (!string.IsNullOrEmpty(response.Body))
            {
                try
                {
                    var json = JObject.Parse(response.Body);
                    message = json["message"]?.ToString() ?? json["error"]?.ToString() ?? message;

                    var code = json["code"]?.Value<int>();
                    if (code.HasValue)
                    {
                        errorCode = (GimErrorCode)code.Value;
                    }
                }
                catch (JsonReaderException)
                {
                    // Body is not valid JSON (e.g., HTML error page), use default error message
                }
            }

            return new GimException(errorCode, message);
        }
    }
}
