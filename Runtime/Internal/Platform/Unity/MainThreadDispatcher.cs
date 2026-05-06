using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace Gamania.GIMChat.Internal.Platform
{
    /// <summary>
    /// Dispatcher to execute callbacks on Unity's main thread.
    /// Solves the issue where callbacks from native platforms are executed on background threads,
    /// which causes crashes when updating UI.
    /// </summary>
    internal class MainThreadDispatcher : MonoBehaviour
    {
        // Singleton instance
        private static MainThreadDispatcher _instance;

        // Queues and callbacks
        private static readonly Queue<Action> _executionQueue = new Queue<Action>();
        private static readonly List<Action> _updateCallbacks = new List<Action>();

        // Thread safety
        private static readonly object _lock = new object();
        private static int? _mainThreadId;

        // Runtime flags
        private static bool? _isTestEnvironment;
        private static bool _isQuitting;

        #region Initialization

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            // Force singleton creation at startup
            var _ = Instance;
        }

        static MainThreadDispatcher()
        {
            // Register cleanup handlers
            Application.quitting += OnApplicationQuit;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        #endregion

        #region Editor Cleanup

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                // Clean up before exiting play mode to avoid "objects not cleaned up" warning
                CleanupInstance();
            }
            else if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                // Reset quitting flag when returning to edit mode (after cleanup)
                _isQuitting = false;
            }
            else if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode)
            {
                // Ensure quitting flag is reset when entering play mode
                _isQuitting = false;
            }
        }

        private static void CleanupInstance()
        {
            // Set quitting flag to prevent re-initialization during cleanup
            _isQuitting = true;

            // Clear queues and reset state
            ClearQueuesAndResetState();

            // Destroy the GameObject instance
            if (_instance != null)
            {
                var instance = _instance;
                _instance = null;

                if (instance != null && instance.gameObject != null)
                {
                    // Use appropriate destroy method based on play mode state
                    if (Application.isPlaying)
                    {
                        Destroy(instance.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(instance.gameObject);
                    }
                }
            }
        }
#endif

        /// <summary>
        /// Clear queues and reset static state
        /// </summary>
        private static void ClearQueuesAndResetState()
        {
            lock (_lock)
            {
                _executionQueue.Clear();
                _updateCallbacks.Clear();
            }

            _mainThreadId = null;
            _isTestEnvironment = null;
        }

        #endregion

        #region Singleton

        public static MainThreadDispatcher Instance
        {
            get
            {
                // Don't create new instance when application is quitting
                if (_isQuitting)
                {
                    return null;
                }

                if (_instance == null)
                {
                    lock (_lock)
                    {
                        // Double-check pattern for thread safety
                        if (_isQuitting)
                        {
                            return null;
                        }

                        if (_instance == null)
                        {
                            var go = new GameObject("GIMChatMainThreadDispatcher");
                            _instance = go.AddComponent<MainThreadDispatcher>();

                            // Capture main thread ID for later thread detection
                            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

                            // Persist across scene loads in play mode
                            if (Application.isPlaying)
                            {
                                DontDestroyOnLoad(go);
                            }
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Lifecycle Events

        private static void OnApplicationQuit()
        {
            // Set quitting flag to prevent new operations
            _isQuitting = true;

            // Clear queues and reset state
            ClearQueuesAndResetState();

            // Reset instance reference (GameObject cleanup handled by Unity)
            _instance = null;
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                ClearQueuesAndResetState();
                _instance = null;
            }
        }

        void Update()
        {
            // Process queued actions - copy queue outside lock to minimize lock duration
            Queue<Action> actionsToExecute = null;
            lock (_lock)
            {
                if (_executionQueue.Count > 0)
                {
                    actionsToExecute = new Queue<Action>(_executionQueue);
                    _executionQueue.Clear();
                }
            }

            // Execute actions outside lock
            if (actionsToExecute != null)
            {
                while (actionsToExecute.Count > 0)
                {
                    var action = actionsToExecute.Dequeue();
                    ExecuteActionSafely(action, "action");
                }
            }

            // Process update callbacks (e.g., WebSocket message dispatch)
            List<Action> callbacksCopy;
            lock (_lock)
            {
                callbacksCopy = new List<Action>(_updateCallbacks);
            }

            foreach (var callback in callbacksCopy)
            {
                ExecuteActionSafely(callback, "update callback");
            }
        }

        #endregion

        #region Core Methods

        /// <summary>
        /// Execute action with exception handling
        /// </summary>
        private static void ExecuteActionSafely(Action action, string actionType)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[GIMChat] MainThreadDispatcher error executing {actionType}: {e}");
            }
        }

        /// <summary>
        /// Enqueue action to be executed on Unity's main thread in the next Update()
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null || _isQuitting) return;

            // Check if we're on the main thread by comparing thread IDs
            // This avoids calling Application.isPlaying from background threads
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            bool isMainThread = _mainThreadId.HasValue && currentThreadId == _mainThreadId.Value;

            // If we're on a background thread, queue immediately
            if (_mainThreadId.HasValue && !isMainThread)
            {
                lock (_lock)
                {
                    _executionQueue.Enqueue(action);
                }
                return;
            }

            // From here, we're either on the main thread or haven't initialized yet
            // Safe to check Application.isPlaying
            bool isPlaying = UnityEngine.Application.isPlaying;

            // EditMode: Execute synchronously (no Update() cycle)
            if (!isPlaying)
            {
                ExecuteActionWithFallbackLogging(action);
                return;
            }

            // PlayMode: Ensure instance exists
            var _ = Instance;

            // In tests on main thread: Execute synchronously for LogAssert
            // Cache the test environment check as it's relatively expensive
            if (!_isTestEnvironment.HasValue)
            {
                _isTestEnvironment = System.AppDomain.CurrentDomain.GetAssemblies()
                    .Any(a => a.FullName.StartsWith("nunit.framework"));
            }

            if (isMainThread && _isTestEnvironment.Value)
            {
                ExecuteActionWithFallbackLogging(action);
                return;
            }

            // Queue for execution in Update()
            lock (_lock)
            {
                _executionQueue.Enqueue(action);
            }
        }

        /// <summary>
        /// Execute action with fallback logging
        /// Used in EditMode where there is no Update() cycle
        /// </summary>
        private static void ExecuteActionWithFallbackLogging(Action action)
        {
            try
            {
                action.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[GIMChat] MainThreadDispatcher error executing action: {e}");
            }
        }

        /// <summary>
        /// Register a callback to be executed every Update cycle
        /// Used for WebSocket message dispatching
        /// </summary>
        public static void RegisterUpdateCallback(Action callback)
        {
            if (callback == null || _isQuitting) return;

            var _ = Instance;

            lock (_lock)
            {
                if (!_updateCallbacks.Contains(callback))
                {
                    _updateCallbacks.Add(callback);
                }
            }
        }

        /// <summary>
        /// Unregister an update callback
        /// </summary>
        public static void UnregisterUpdateCallback(Action callback)
        {
            if (callback == null) return;

            lock (_lock)
            {
                _updateCallbacks.Remove(callback);
            }
        }

        #endregion

        #region Testing Support

#if UNITY_INCLUDE_TESTS
        /// <summary>
        /// Clear all pending actions in the queue (for testing purposes only)
        /// </summary>
        internal static void ClearQueue()
        {
            var _ = Instance;
            lock (_lock)
            {
                _executionQueue.Clear();
            }
        }

        /// <summary>
        /// Process all pending actions in the queue (for Editor tests without Update loop)
        /// </summary>
        internal static void ProcessQueue()
        {
            var _ = Instance;
            while (true)
            {
                Action action;
                lock (_lock)
                {
                    if (_executionQueue.Count == 0) break;
                    action = _executionQueue.Dequeue();
                }
                ExecuteActionWithFallbackLogging(action);
            }
        }
#endif

        #endregion
    }
}
