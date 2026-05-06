using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;
using Gamania.GIMChat.Internal.Data.Network;
using Gamania.GIMChat.Internal.Domain.Log;

namespace Gamania.GIMChat.Internal.Platform.Unity.Network
{
    /// <summary>
    /// Unity-based HTTP client implementation using UnityWebRequest
    /// </summary>
    public class UnityHttpClient : IHttpClient
    {
        private string _sessionKey;
        private const string SessionKeyHeader = "Session-Key";
        private const int DefaultTimeoutSeconds = 30;

        /// <summary>
        /// Sets the session key that will be included in request headers
        /// </summary>
        public void SetSessionKey(string sessionKey)
        {
            _sessionKey = sessionKey;
            Logger.Debug(LogCategory.Http, $"Session key set: {sessionKey}");
        }

        public async Task<HttpResponse> GetAsync(
            string url,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default)
        {
            using var request = UnityWebRequest.Get(url);
            return await SendRequestAsync(request, headers, cancellationToken);
        }

        public async Task<HttpResponse> PostAsync(
            string url,
            string body,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default)
        {
            using var request = new UnityWebRequest(url, "POST");
            if (!string.IsNullOrEmpty(body))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.SetRequestHeader("Content-Type", "application/json");
            }

            request.downloadHandler = new DownloadHandlerBuffer();

            return await SendRequestAsync(request, headers, cancellationToken);
        }

        public async Task<HttpResponse> PutAsync(
            string url,
            string body,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default)
        {
            using var request = new UnityWebRequest(url, "PUT");
            if (!string.IsNullOrEmpty(body))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.SetRequestHeader("Content-Type", "application/json");
            }

            request.downloadHandler = new DownloadHandlerBuffer();

            return await SendRequestAsync(request, headers, cancellationToken);
        }

        public async Task<HttpResponse> PutBytesAsync(
            string url,
            byte[] data,
            string contentType = null,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default)
        {
            using var request = new UnityWebRequest(url, "PUT");
            if (data != null && data.Length > 0)
            {
                request.uploadHandler = new UploadHandlerRaw(data);
                if (!string.IsNullOrEmpty(contentType))
                {
                    request.SetRequestHeader("Content-Type", contentType);
                }
            }

            request.downloadHandler = new DownloadHandlerBuffer();

            return await SendRequestAsync(request, headers, cancellationToken);
        }

        public async Task<HttpResponse> PutFileAsync(
            string url,
            string filePath,
            string contentType = null,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default)
        {
            using var request = new UnityWebRequest(url, "PUT");
            if (!string.IsNullOrEmpty(filePath))
            {
                request.uploadHandler = new UploadHandlerFile(filePath);
                if (!string.IsNullOrEmpty(contentType))
                {
                    request.SetRequestHeader("Content-Type", contentType);
                }
            }

            request.downloadHandler = new DownloadHandlerBuffer();

            return await SendRequestAsync(request, headers, cancellationToken);
        }

        public async Task<HttpResponse> DeleteAsync(
            string url,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default)
        {
            using var request = UnityWebRequest.Delete(url);
            request.downloadHandler = new DownloadHandlerBuffer();
            return await SendRequestAsync(request, headers, cancellationToken);
        }

        private async Task<HttpResponse> SendRequestAsync(
            UnityWebRequest request,
            Dictionary<string, string> headers,
            CancellationToken cancellationToken)
        {
            try
            {
                request.timeout = DefaultTimeoutSeconds;

                request.SetRequestHeader(ApiVersionConfig.HeaderName, ApiVersionConfig.DefaultVersion);

                if (!string.IsNullOrEmpty(_sessionKey))
                {
                    request.SetRequestHeader(SessionKeyHeader, _sessionKey);
                }

                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.SetRequestHeader(header.Key, header.Value);
                    }
                }

                var asyncOperation = request.SendWebRequest();

                while (!asyncOperation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    await Task.Yield();
                }

                var response = new HttpResponse
                {
                    StatusCode = (int)request.responseCode,
                    Body = request.downloadHandler?.text ?? string.Empty
                };

                var responseHeaders = request.GetResponseHeaders();
                if (responseHeaders != null)
                {
                    foreach (var header in responseHeaders)
                    {
                        response.Headers[header.Key] = header.Value;
                    }
                }

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError ||
                    request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    response.Error = request.error;
                }

                return response;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new HttpResponse
                {
                    StatusCode = 0,
                    Error = $"HTTP request failed: {ex.Message}",
                    Body = string.Empty
                };
            }
        }
    }
}
