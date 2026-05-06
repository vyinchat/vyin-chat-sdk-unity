using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Gamania.GIMChat;
using System;
using System.Collections.Generic;

/// <summary>
/// MainPage — SDK init, connect, channel management, and ChannelCollection.
/// After channel is ready, opens ChatPage via ChatPageController.
/// </summary>
public class VyinChatSampleController : MonoBehaviour, IGimSessionHandler, IGimGroupChannelCollectionDelegate
{
    #region Inspector Fields

    [Header("SDK Configuration")]
    [Tooltip("App ID from the Vyin Chat console (default: PROD)")]
    [SerializeField] private string appId = "adb53e88-4c35-469a-a888-9e49ef1641b2";

    [Tooltip("User ID for testing")]
    [SerializeField] private string userId = "testuser1";

    [Tooltip("Auth Token (optional, leave empty if not needed)")]
    [SerializeField] private string authToken = "";

    [Header("Channel Configuration")]
    [Tooltip("Channel name to create")]
    [SerializeField] private string channelName = "Unity Test Channel";

    [Tooltip("Bot ID to invite to the channel")]
    [SerializeField] private string botId = "vyin_chat_openai";

    [Tooltip("Other users to invite to the channel (optional)")]
    [SerializeField] private List<string> inviteUserIds = new();

    [Header("Collection Configuration")]
    [Tooltip("Number of channels to load per page in collection")]
    [SerializeField] private int pageLimit = 10;

    [Header("Debug")]
    [Tooltip("Enable automatic message resend on reconnection")]
    [SerializeField] private bool enableAutoResend = true;

    [Tooltip("New token to provide when SDK requests refresh")]
    [SerializeField] private string refreshToken = "demo-refresh-token";

    [Tooltip("Simulate token refresh failure")]
    [SerializeField] private bool simulateRefreshFailure;

    [Header("UI Elements")]
    [SerializeField] private Transform logContent;
    [SerializeField] private ScrollRect logScrollRect;
    [SerializeField] private TextMeshProUGUI connectionStatusText;
    [SerializeField] private Button loadMoreButton;
    [SerializeField] private Button openChatButton;

    [Header("Prefabs")]
    [SerializeField] private TextMeshProUGUI logItemPrefab;

    [Header("Chat Page")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private ChatPageController chatPageController;

    #endregion

    #region Private Fields

    private const string HANDLER_ID = "VyinChatSampleHandler";
    private GimGroupChannel _currentChannel;
    private GimGroupChannelCollection _channelCollection;

    // Token refresh state
    private Action<string> _tokenRefreshSuccess;
    private Action _tokenRefreshFail;
    private bool _isWaitingForToken;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        SetupUI();
        InitializeAndConnect();
    }

    private void OnDestroy()
    {
        GimGroupChannel.RemoveGroupChannelHandler(HANDLER_ID);
        GIMChat.RemoveConnectionHandler(HANDLER_ID);
        _channelCollection?.Dispose();
    }

    #endregion

    #region Step 1: Initialize

    private void InitializeAndConnect()
    {
        LogInfo($"AppId: {appId}");

        var initParams = new GimInitParams(appId, logLevel: GimLogLevel.Debug);
        GIMChat.Init(initParams);
        LogInfo("SDK Initialized");

        GIMChat.SetEnableMessageAutoResend(enableAutoResend);
        LogInfo($"Auto-resend: {(enableAutoResend ? "enabled" : "disabled")}");

        GIMChat.SetSessionHandler(this);
        LogInfo("Session handler registered");

        RegisterConnectionHandler();
        Connect();
    }

    #endregion

    #region Step 2: Connect

    private void Connect()
    {
        var token = string.IsNullOrEmpty(authToken) ? null : authToken;
        LogInfo($"Connecting as '{userId}'...");
        UpdateConnectionStatusDisplay("Connecting...", ConnectingColor);
        GIMChat.Connect(userId, token, OnConnected);
    }

    private void OnConnected(GimUser user, GimException error)
    {
        if (error != null)
        {
            LogError($"Connection failed: {error.Message}");
            return;
        }

        LogInfo($"Connected! Welcome, {user.UserId}!");
        InitChannelCollectionAndLoad();
        CreateOrGetChannel();
    }

    #endregion

    #region Step 3: Channel

    private void CreateOrGetChannel()
    {
        LogSeparator();
        LogInfo($"Creating channel '{channelName}'...");

        var members = new List<string> { userId };
        if (!string.IsNullOrEmpty(botId))
        {
            LogInfo($"Inviting bot: '{botId}'");
            members.Add(botId);
        }
        members.AddRange(inviteUserIds);

        var createParams = new GimGroupChannelCreateParams
        {
            Name = channelName,
            UserIds = members,
            OperatorUserIds = new List<string> { userId },
            IsDistinct = true
        };

        GimGroupChannelModule.CreateGroupChannel(createParams, OnChannelCreated);
    }

    private void OnChannelCreated(GimGroupChannel channel, GimException error)
    {
        if (error != null)
        {
            LogError($"Failed to create channel: {error.Message}");
            return;
        }

        LogInfo("Channel created!");
        GimGroupChannelModule.GetGroupChannel(channel.ChannelUrl, OnChannelRetrieved);
    }

    private void OnChannelRetrieved(GimGroupChannel channel, GimException error)
    {
        if (error != null)
        {
            LogError($"Failed to get channel: {error.Message}");
            return;
        }

        _currentChannel = channel;
        LogInfo($"Channel ready: {channel.Name}");
        LogInfo($"  URL: {channel.ChannelUrl}");
        LogSeparator();
        LogInfo("Tap 'Open Chat' to enter the chat room.");

        if (openChatButton != null)
            openChatButton.interactable = true;

        OnSampleGroupChannelReady(channel);
    }

    protected virtual void OnSampleGroupChannelReady(GimGroupChannel channel) { }

    #endregion

    #region Step 4: ChannelCollection

    private async void InitChannelCollectionAndLoad()
    {
        LogSeparator();
        LogInfo($"Creating GroupChannelCollection (limit={pageLimit})...");

        _channelCollection = GIMChat.CreateGroupChannelCollection(pageLimit);
        _channelCollection.Delegate = this;

        try
        {
            var channels = await _channelCollection.LoadMoreAsync();
            LogInfo($"Collection loaded {channels.Count} channels.");
        }
        catch (GimException ex)
        {
            LogError($"Collection Load failed: {ex.Message}");
        }
    }

    [ContextMenu("Test LoadMore Channels")]
    private async void TestLoadMoreChannels()
    {
        if (_channelCollection == null)
        {
            LogError("[TestLoadMore] Collection not created yet!");
            return;
        }

        if (!_channelCollection.HasNext)
        {
            LogInfo("[TestLoadMore] No more channels to load!");
            return;
        }

        if (_channelCollection.IsLoading)
        {
            LogInfo("[TestLoadMore] Already loading...");
            return;
        }

        LogSeparator();
        try
        {
            var channels = await _channelCollection.LoadMoreAsync();
            LogInfo($"[TestLoadMore] Loaded {channels.Count} channel(s). Total: {_channelCollection.ChannelList.Count}");
            foreach (var ch in channels)
            {
                var lastMsg = ch.LastMessage?.Message ?? "(no messages)";
                LogInfo($"  {ch.Name ?? ch.ChannelUrl} | {lastMsg}");
            }
        }
        catch (GimException ex)
        {
            LogError($"[TestLoadMore] Failed: {ex.Message}");
        }
    }

    #endregion

    #region IGimGroupChannelCollectionDelegate

    public void OnChannelsAdded(GimGroupChannelCollection collection, GimChannelContext context, IReadOnlyList<GimGroupChannel> channels)
    {
        LogInfo($"[ChannelCollection] Added {channels.Count} channel(s), Source: {context.Source}");
    }

    public void OnChannelsUpdated(GimGroupChannelCollection collection, GimChannelContext context, IReadOnlyList<GimGroupChannel> channels)
    {
        LogInfo($"[ChannelCollection] Updated {channels.Count} channel(s), Source: {context.Source}");
    }

    public void OnChannelsDeleted(GimGroupChannelCollection collection, GimChannelContext context, IReadOnlyList<string> channelUrls)
    {
        LogInfo($"[ChannelCollection] Deleted {channelUrls.Count} channel(s), Source: {context.Source}");
    }

    #endregion

    #region UI

    private void SetupUI()
    {
        if (loadMoreButton != null)
            loadMoreButton.onClick.AddListener(TestLoadMoreChannels);

        if (openChatButton != null)
        {
            openChatButton.interactable = false;
            openChatButton.onClick.AddListener(OnOpenChatClicked);
        }

        UpdateConnectionStatusDisplay("Disconnected", DisconnectedColor);
    }

    private void OnOpenChatClicked()
    {
        if (_currentChannel == null) return;
        mainPanel?.SetActive(false);
        chatPageController.Open(_currentChannel, userId, onClose: () => mainPanel?.SetActive(true));
    }

    private static readonly Color ConnectedColor = new(0.2f, 0.8f, 0.4f);
    private static readonly Color DisconnectedColor = new(0.9f, 0.3f, 0.3f);
    private static readonly Color ConnectingColor = new(0.3f, 0.6f, 0.9f);
    private static readonly Color ReconnectingColor = new(1f, 0.7f, 0.2f);

    private void RegisterConnectionHandler()
    {
        var handler = new GimConnectionHandler
        {
            OnConnected = _ => UpdateConnectionStatusDisplay("Connected", ConnectedColor),
            OnDisconnected = _ => UpdateConnectionStatusDisplay("Disconnected", DisconnectedColor),
            OnReconnectStarted = () => UpdateConnectionStatusDisplay("Reconnecting...", ReconnectingColor),
            OnReconnectSucceeded = () => UpdateConnectionStatusDisplay("Connected", ConnectedColor),
            OnReconnectFailed = () => UpdateConnectionStatusDisplay("Reconnect Failed", DisconnectedColor)
        };
        GIMChat.AddConnectionHandler(HANDLER_ID, handler);
    }

    private void UpdateConnectionStatusDisplay(string text, Color color)
    {
        if (connectionStatusText == null) return;
        connectionStatusText.text = text;
        connectionStatusText.color = color;
    }

    #endregion

    #region Logging

    protected void LogInfo(string message)
    {
        var formatted = $"[Sample] {message}";
        Debug.Log(formatted);
        AppendLogItem(formatted);
    }

    protected void LogError(string message)
    {
        var formatted = $"[Sample] ERROR: {message}";
        Debug.LogError(formatted);
        AppendLogItem(formatted);
    }

    protected void LogSeparator()
    {
        AppendLogItem("────────────────────────────────");
    }

    private void AppendLogItem(string text)
    {
        if (logItemPrefab == null || logContent == null) return;
        var item = Instantiate(logItemPrefab, logContent);
        item.text = text;
        ScrollLogToBottom();
    }

    private void ScrollLogToBottom()
    {
        if (logScrollRect != null)
            StartCoroutine(ScrollToBottomCoroutine());
    }

    private System.Collections.IEnumerator ScrollToBottomCoroutine()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        logScrollRect.verticalNormalizedPosition = 0f;
    }

    #endregion

    #region IGimSessionHandler

    public void OnSessionTokenRequired(Action<string> success, Action fail)
    {
        _tokenRefreshSuccess = success;
        _tokenRefreshFail = fail;
        _isWaitingForToken = true;

        LogInfo("SDK requests new token!");
        if (!simulateRefreshFailure && !string.IsNullOrEmpty(refreshToken))
        {
            LogInfo($"Auto-providing token...");
            ProvideRefreshToken();
        }
    }

    public void OnSessionRefreshed()
    {
        LogInfo("Session refreshed successfully!");
        _isWaitingForToken = false;
    }

    public void OnSessionClosed()
    {
        LogError("Session closed - would navigate to login");
        _isWaitingForToken = false;
    }

    public void OnSessionError(GimException error)
    {
        LogError($"Session error: {error.ErrorCode} - {error.Message}");
        _isWaitingForToken = false;
    }

    public void ProvideRefreshToken()
    {
        if (!_isWaitingForToken) return;
        _isWaitingForToken = false;
        _tokenRefreshSuccess?.Invoke(refreshToken);
    }

    public void FailTokenRefresh()
    {
        if (!_isWaitingForToken) return;
        _isWaitingForToken = false;
        _tokenRefreshFail?.Invoke();
    }

    #endregion
}
