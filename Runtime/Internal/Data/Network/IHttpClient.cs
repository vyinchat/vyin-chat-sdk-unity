using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Gamania.GIMChat.Internal.Data.Network
{
    /// <summary>
    /// HTTP Client interface for making REST API calls
    /// Platform abstraction to allow different implementations (UnityWebRequest, HttpClient, etc.)
    /// </summary>
    public interface IHttpClient
    {
        /// <summary>
        /// Sets the session key for authenticated requests
        /// Session key will be automatically included in all subsequent requests
        /// </summary>
        /// <param name="sessionKey">Session key from authentication</param>
        void SetSessionKey(string sessionKey);

        /// <summary>
        /// Performs a GET request
        /// </summary>
        /// <param name="url">Full URL to request</param>
        /// <param name="headers">Optional HTTP headers</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>HTTP response</returns>
        Task<HttpResponse> GetAsync(
            string url,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a POST request
        /// </summary>
        /// <param name="url">Full URL to request</param>
        /// <param name="body">Request body (JSON string)</param>
        /// <param name="headers">Optional HTTP headers</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>HTTP response</returns>
        Task<HttpResponse> PostAsync(
            string url,
            string body,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a PUT request
        /// </summary>
        Task<HttpResponse> PutAsync(
            string url,
            string body,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a PUT request with raw binary body.
        /// Used for uploading file bytes to presigned URLs.
        /// </summary>
        /// <param name="url">Full URL to request</param>
        /// <param name="data">Raw binary data to upload</param>
        /// <param name="contentType">Content-Type header value (e.g. "image/png")</param>
        /// <param name="headers">Optional HTTP headers</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>HTTP response</returns>
        Task<HttpResponse> PutBytesAsync(
            string url,
            byte[] data,
            string contentType = null,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a PUT request with a local file.
        /// Used for uploading large files to presigned URLs via streaming.
        /// </summary>
        /// <param name="url">Full URL to request</param>
        /// <param name="filePath">Local path to the file to upload</param>
        /// <param name="contentType">Content-Type header value</param>
        /// <param name="headers">Optional HTTP headers</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>HTTP response</returns>
        Task<HttpResponse> PutFileAsync(
            string url,
            string filePath,
            string contentType = null,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a DELETE request
        /// </summary>
        Task<HttpResponse> DeleteAsync(
            string url,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default);
    }
}
