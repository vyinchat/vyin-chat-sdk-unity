using System;
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
