using System.Collections.Generic;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Callback handler for user-related operations
    /// </summary>
    /// <param name="user">The user object if the operation succeeded, null otherwise</param>
    /// <param name="error">The exception if the operation failed, null otherwise</param>
    public delegate void GimUserHandler(GimUser user, GimException error);

    /// <summary>
    /// Callback handler for message-related operations
    /// </summary>
    /// <param name="message">The message object if the operation succeeded, null otherwise</param>
    /// <param name="error">The exception if the operation failed, null otherwise</param>
    public delegate void GimUserMessageHandler(GimUserMessage message, GimException error);

    /// <summary>
    /// Callback handler for group channel operations
    /// </summary>
    /// <param name="channel">The group channel object if the operation succeeded, null otherwise</param>
    /// <param name="error">The exception if the operation failed, null otherwise</param>
    public delegate void GimGroupChannelCallbackHandler(GimGroupChannel channel, GimException error);

    // ── Collection callbacks ─────────────────────────────────────────────────────

    /// <summary>
    /// Callback for message list operations (pagination, startCollection).
    /// </summary>
    /// <param name="messages">Loaded messages, or null on error.</param>
    /// <param name="error">The exception if the operation failed, null on success.</param>
    public delegate void GimMessageListHandler(IReadOnlyList<GimBaseMessage> messages, GimException error);

    /// <summary>
    /// Callback for channel list operations (LoadMore).
    /// </summary>
    /// <param name="channels">Loaded channels, or null on error.</param>
    /// <param name="error">The exception if the operation failed, null on success.</param>
    public delegate void GimGroupChannelListHandler(IReadOnlyList<GimGroupChannel> channels, GimException error);

    /// <summary>
    /// Generic error-only callback (e.g., RemoveFailed, RemoveAllFailed).
    /// </summary>
    /// <param name="error">The exception if the operation failed, null on success.</param>
    public delegate void GimErrorHandler(GimException error);

    // ── File message callbacks ──────────────────────────────────────────────────

    /// <summary>
    /// Callback handler for file message operations (no progress).
    /// </summary>
    /// <param name="message">The file message object if the operation succeeded, null otherwise.</param>
    /// <param name="error">The exception if the operation failed, null otherwise.</param>
    public delegate void GimFileMessageHandler(GimFileMessage message, GimException error);

    /// <summary>
    /// Handler with upload progress callback for file message operations.
    /// </summary>
    public interface IGimFileMessageWithProgressHandler
    {
        /// <summary>
        /// Called during file upload to report progress. Dispatched on Unity main thread.
        /// </summary>
        /// <param name="bytesSent">Bytes sent in this chunk.</param>
        /// <param name="totalBytesSent">Cumulative bytes sent so far.</param>
        /// <param name="totalBytesToSend">Total file size in bytes.</param>
        void OnProgress(int bytesSent, int totalBytesSent, int totalBytesToSend);

        /// <summary>
        /// Called when the send operation completes (success or failure).
        /// </summary>
        /// <param name="message">The file message if succeeded, null otherwise.</param>
        /// <param name="error">The exception if failed, null otherwise.</param>
        void OnResult(GimFileMessage message, GimException error);
    }

    /// <summary>
    /// Action-based convenience implementation of IGimFileMessageWithProgressHandler.
    /// </summary>
    public class GimFileMessageWithProgressHandler : IGimFileMessageWithProgressHandler
    {
        /// <summary>Action invoked for progress updates (bytesSent, totalBytesSent, totalBytesToSend).</summary>
        public System.Action<int, int, int> OnProgressAction { get; set; }

        /// <summary>Action invoked for send result.</summary>
        public System.Action<GimFileMessage, GimException> OnResultAction { get; set; }

        public void OnProgress(int bytesSent, int totalBytesSent, int totalBytesToSend)
            => OnProgressAction?.Invoke(bytesSent, totalBytesSent, totalBytesToSend);

        public void OnResult(GimFileMessage message, GimException error)
            => OnResultAction?.Invoke(message, error);
    }
}
