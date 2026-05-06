using System;

namespace Gamania.GIMChat.Internal.Domain.Message
{
    /// <summary>
    /// Represents a message pending for send or resend.
    /// Tracks state, retry count, TTL, and callback.
    /// </summary>
    public class PendingMessage
    {
        /// <summary>Maximum retry attempts.</summary>
        public const int MaxRetries = 3;

        /// <summary>TTL duration in hours.</summary>
        public const int TtlHours = 24;

        /// <summary>Base delay for exponential backoff in ms.</summary>
        public const int BaseDelayMs = 1000;

        /// <summary>Maximum jitter added to backoff delay in ms.</summary>
        public const int MaxJitterMs = 200;

        private static readonly Random _jitterRandom = new Random();

        /// <summary>
        /// Unique identifier for matching ACK responses.
        /// </summary>
        public string RequestId { get; }

        /// <summary>
        /// Message creation parameters.
        /// Accepts GimBaseMessageCreateParams to support both UserMessage and FileMessage.
        /// </summary>
        public GimBaseMessageCreateParams CreateParams { get; }

        /// <summary>
        /// Current sending status.
        /// </summary>
        public GimSendingStatus Status { get; private set; } = GimSendingStatus.Pending;

        /// <summary>
        /// Error code if failed. Null means no error.
        /// </summary>
        public GimErrorCode? ErrorCode { get; set; }

        /// <summary>
        /// Number of retry attempts.
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// Timestamp when message was first created (UTC ticks).
        /// </summary>
        public long CreatedAtTicks { get; set; }

        /// <summary>
        /// Callback to invoke on success or failure.
        /// </summary>
        public Action<PendingMessage, GimException> OnFailed { get; set; }

        /// <summary>
        /// Callback to invoke on success.
        /// </summary>
        public Action<GimBaseMessage> OnSuccess { get; set; }

        /// <summary>
        /// Event triggered when sending status changes.
        /// Provides the channel URL, base message, and new status.
        /// </summary>
        public Action<string, GimBaseMessage, GimSendingStatus> OnStatusChanged { get; set; }

        /// <summary>Linked base message for status synchronization.</summary>
        public GimBaseMessage BaseMessage { get; }

        /// <summary>
        /// Creates a new pending message.
        /// </summary>
        public PendingMessage(string requestId, GimBaseMessageCreateParams createParams, GimBaseMessage baseMessage)
        {
            RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
            BaseMessage = baseMessage ?? throw new ArgumentNullException(nameof(baseMessage));
            CreateParams = createParams;
            CreatedAtTicks = DateTime.UtcNow.Ticks;
            Status = GimSendingStatus.Pending;
            SyncToBaseMessage();
        }

        /// <summary>
        /// ChannelUrl resolved from BaseMessage.
        /// </summary>
        public string ChannelUrl => BaseMessage.ChannelUrl;

        /// <summary>
        /// Check if message has exceeded TTL (24 hours).
        /// Returns true if elapsed time is greater than TTL.
        /// At exactly 24 hours, message is considered expired.
        /// </summary>
        public bool IsExpired()
        {
            var elapsed = DateTime.UtcNow.Ticks - CreatedAtTicks;
            var ttlTicks = TimeSpan.FromHours(TtlHours).Ticks;
            return elapsed >= ttlTicks;
        }

        /// <summary>
        /// Check if message can be retried (under max retries and not expired).
        /// </summary>
        public bool CanRetry()
        {
            return RetryCount < MaxRetries && !IsExpired();
        }

        /// <summary>
        /// Increment retry count.
        /// </summary>
        public void IncrementRetry()
        {
            RetryCount++;
        }

        /// <summary>
        /// Get exponential backoff delay with jitter.
        /// Formula: (BaseDelay * 2^RetryCount) + random(0, MaxJitter)
        /// </summary>
        public int GetBackoffDelayMs()
        {
            var exponentialDelay = BaseDelayMs * (1 << RetryCount); // 2^RetryCount
            var jitter = _jitterRandom.Next(0, MaxJitterMs + 1);
            return exponentialDelay + jitter;
        }

        /// <summary>
        /// Check if error code is auto-resendable.
        /// </summary>
        public bool IsAutoResendable => ErrorCode.HasValue && ErrorCode.Value.IsAutoResendable();

        /// <summary>
        /// Transition to Sending status.
        /// </summary>
        public void MarkAsPending()
        {
            if (Status.CanTransitionTo(GimSendingStatus.Pending))
            {
                Status = GimSendingStatus.Pending;
                SyncToBaseMessage();
            }
        }

        /// <summary>
        /// Transition to Succeeded status.
        /// </summary>
        public void MarkAsSucceeded()
        {
            if (Status.CanTransitionTo(GimSendingStatus.Succeeded))
            {
                Status = GimSendingStatus.Succeeded;
                SyncToBaseMessage();
            }
        }

        /// <summary>
        /// Transition to Failed status with error code.
        /// </summary>
        public void MarkAsFailed(GimErrorCode errorCode)
        {
            if (Status.CanTransitionTo(GimSendingStatus.Failed))
            {
                Status = GimSendingStatus.Failed;
                ErrorCode = errorCode;
                SyncToBaseMessage();
            }
        }

        /// <summary>
        /// Transition to Canceled status.
        /// </summary>
        public void MarkAsCanceled()
        {
            if (Status.CanTransitionTo(GimSendingStatus.Canceled))
            {
                Status = GimSendingStatus.Canceled;
                SyncToBaseMessage();
            }
        }

        private void SyncToBaseMessage()
        {
            BaseMessage.SendingStatus = Status;
            BaseMessage.ErrorCode = ErrorCode;
            BaseMessage.ReqId = RequestId;

            // Notify observers of status change
            OnStatusChanged?.Invoke(ChannelUrl, BaseMessage, Status);
        }
    }
}
