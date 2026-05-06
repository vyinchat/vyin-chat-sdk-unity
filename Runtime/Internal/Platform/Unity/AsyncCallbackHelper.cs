using System;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Log;

namespace Gamania.GIMChat.Internal.Platform.Unity
{
    /// <summary>
    /// Helper for executing async operations with callback pattern.
    /// Ensures callbacks are always invoked on the main thread.
    /// </summary>
    internal static class AsyncCallbackHelper
    {
        /// <summary>
        /// Executes an async operation and invokes callback with result or error.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="asyncOperation">The async operation to execute.</param>
        /// <param name="callback">Callback invoked with result or error.</param>
        /// <param name="tag">Log tag for error logging.</param>
        /// <param name="operationName">Operation name for error logging.</param>
        public static async Task ExecuteAsync<T>(
            Func<Task<T>> asyncOperation,
            Action<T, GimException> callback,
            string tag,
            string operationName)
        {
            void InvokeCallback(T result, GimException error)
            {
                if (error != null)
                {
                    Logger.Error(tag, $"{operationName} failed", error);
                }
                MainThreadDispatcher.Enqueue(() => callback?.Invoke(result, error));
            }

            try
            {
                var result = await asyncOperation();
                InvokeCallback(result, null);
            }
            catch (GimException vcEx)
            {
                InvokeCallback(default, vcEx);
            }
            catch (Exception ex)
            {
                var fallback = new GimException(GimErrorCode.UnknownError, $"Unexpected error: {ex.Message}", ex);
                InvokeCallback(default, fallback);
            }
        }

        /// <summary>
        /// Executes an async void operation and invokes callback with error (null on success).
        /// </summary>
        public static async Task ExecuteVoidAsync(
            Func<Task> asyncOperation,
            GimErrorHandler callback,
            string tag,
            string operationName)
        {
            void InvokeCallback(GimException error)
            {
                if (error != null)
                {
                    Logger.Error(tag, $"{operationName} failed", error);
                }
                MainThreadDispatcher.Enqueue(() => callback?.Invoke(error));
            }

            try
            {
                await asyncOperation();
                InvokeCallback(null);
            }
            catch (GimException vcEx)
            {
                InvokeCallback(vcEx);
            }
            catch (Exception ex)
            {
                var fallback = new GimException(GimErrorCode.UnknownError, $"Unexpected error: {ex.Message}", ex);
                InvokeCallback(fallback);
            }
        }
    }
}
