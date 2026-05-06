using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Gamania.GIMChat;
using System.Collections.Generic;

/// <summary>
/// ChatPage — MessageCollection lifecycle, message list display, and message sending.
/// Opened by VyinChatSampleController.OpenChat(channel) after channel is ready.
/// </summary>
public class ChatPageController : MonoBehaviour, IGimMessageCollectionDelegate
{
    #region Inspector Fields

    [Header("UI Panels")]
    [SerializeField] private GameObject chatPanel;

    [Header("Message List")]
    [SerializeField] private Transform messageListContent;
    [SerializeField] private ScrollRect messageScrollRect;
    [SerializeField] private Button loadPreviousButton;

    [Header("Input")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button sendButton;
    [SerializeField] private Button backButton;

    [Header("Prefabs")]
    [SerializeField] private MessageItemController messageItemPrefab;

    #endregion

    #region Private Fields

    private GimGroupChannel _channel;
    private GimMessageCollection _messageCollection;
    private string _currentUserId;
    private System.Action _onClose;

    // messageId → MessageItemController for in-place update
    private readonly Dictionary<string, MessageItemController> _messageItems = new();

    #endregion

    #region Public API

    /// <summary>
    /// Called by VyinChatSampleController when channel is ready.
    /// </summary>
    public void Open(GimGroupChannel channel, string userId, System.Action onClose = null)
    {
        _channel = channel;
        _currentUserId = userId;
        _onClose = onClose;
        Debug.Log($"[ChatPage] Open() called, chatPanel={chatPanel?.name}");
        chatPanel.SetActive(true);
        Debug.Log($"[ChatPage] chatPanel.activeSelf={chatPanel.activeSelf}");
        StartMessageCollection();
    }

    public void Close()
    {
        chatPanel.SetActive(false);
        DisposeCollection();
        _onClose?.Invoke();
    }

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        sendButton.onClick.AddListener(OnSendClicked);
        backButton.onClick.AddListener(Close);
        loadPreviousButton.onClick.AddListener(OnLoadPreviousClicked);
    }

    private void OnDestroy()
    {
        DisposeCollection();
    }

    #endregion

    #region MessageCollection

    private void StartMessageCollection()
    {
        DisposeCollection();
        ClearMessageList();

        _messageCollection = GIMChat.CreateMessageCollection(_channel);
        _messageCollection.Delegate = this;

        _messageCollection.StartCollection((messages, error) =>
        {
            if (error != null)
            {
                Debug.LogError($"[ChatPage] StartCollection failed: {error.Message}");
                return;
            }

            foreach (var msg in messages)
                AddOrUpdateItem(msg, source: "MessageList");

            RefreshLoadPreviousButton();
            ScrollToBottom();
        });
    }

    private async void OnLoadPreviousClicked()
    {
        if (_messageCollection == null || !_messageCollection.HasPrevious) return;

        loadPreviousButton.interactable = false;
        try
        {
            var messages = await _messageCollection.LoadPreviousAsync();
            // Prepend older messages at top — insert before existing items
            for (int i = messages.Count - 1; i >= 0; i--)
                PrependItem(messages[i]);
        }
        catch (GimException ex)
        {
            Debug.LogError($"[ChatPage] LoadPrevious failed: {ex.Message}");
        }
        finally
        {
            RefreshLoadPreviousButton();
        }
    }

    private void DisposeCollection()
    {
        _messageCollection?.Dispose();
        _messageCollection = null;
    }

    #endregion

    #region IGimMessageCollectionDelegate

    public void OnMessagesAdded(GimMessageCollection collection, GimMessageContext context, GimGroupChannel channel, IReadOnlyList<GimBaseMessage> addedMessages)
    {
        foreach (var msg in addedMessages)
        {
            if (context.Source == GimCollectionEventSource.LocalMessagePendingCreated) continue;
            if (msg.Sender?.UserId == _currentUserId) continue;
            AddOrUpdateItem(msg, source: context.Source.ToString());
        }
        ScrollToBottom();
    }

    public void OnMessagesUpdated(GimMessageCollection collection, GimMessageContext context, GimGroupChannel channel, IReadOnlyList<GimBaseMessage> updatedMessages)
    {
        foreach (var msg in updatedMessages)
        {
            var key = msg.MessageId.ToString();
            if (msg.Sender?.UserId == _currentUserId && !_messageItems.ContainsKey(key)) continue;
            AddOrUpdateItem(msg, source: context.Source.ToString());
        }
    }

    public void OnMessagesDeleted(GimMessageCollection collection, GimMessageContext context, GimGroupChannel channel, IReadOnlyList<GimBaseMessage> deletedMessages)
    {
        foreach (var msg in deletedMessages)
        {
            if (_messageItems.TryGetValue(msg.MessageId.ToString(), out var item))
                item.MarkDeleted();
        }
    }

    public void OnChannelUpdated(GimMessageCollection collection, GimMessageContext context, GimGroupChannel channel) { }

    public void OnChannelDeleted(GimMessageCollection collection, GimMessageContext context, string channelUrl)
    {
        Close();
    }

    public void OnHugeGapDetected(GimMessageCollection collection)
    {
        Debug.Log("[ChatPage] HugeGap detected, restarting collection.");
        StartMessageCollection();
    }

    #endregion

    #region Send Message

    private void OnSendClicked()
    {
        if (inputField == null) return;
        var text = inputField.text.Trim();
        if (string.IsNullOrEmpty(text) || _channel == null) return;

        inputField.text = "";
        inputField.ActivateInputField();

        var msgParams = new GimUserMessageCreateParams { Message = text };
        GimUserMessage pending = null;
        pending = _channel.SendUserMessage(msgParams, (msg, err) => OnMessageSent(msg, err, pending?.ReqId, text));

        if (pending != null)
            ShowPending(pending.ReqId, text);
    }

    private void OnMessageSent(GimUserMessage message, GimException error, string pendingId, string originalText)
    {
        if (error != null)
        {
            if (!string.IsNullOrEmpty(pendingId) && _messageItems.TryGetValue(pendingId, out var failedItem))
                failedItem.SetStatus(GimSendingStatus.Failed);
            return;
        }

        if (message == null) return;

        // Replace pending item with confirmed message
        if (!string.IsNullOrEmpty(pendingId) && _messageItems.TryGetValue(pendingId, out var pendingItem))
        {
            _messageItems.Remove(pendingId);
            _messageItems[message.MessageId.ToString()] = pendingItem;
            pendingItem.Bind(message);
            Debug.Log($"[ChatPage] OnMessageSent: replaced pendingId={pendingId} -> messageId={message.MessageId}");
        }
        else
        {
            Debug.Log($"[ChatPage] OnMessageSent: pendingId={pendingId} not found in cache, adding new");
            AddOrUpdateItem(message);
        }
    }

    private void ShowPending(string reqId, string text)
    {
        var item = Instantiate(messageItemPrefab, messageListContent);
        item.BindPending(reqId, _currentUserId, text);
        _messageItems[reqId] = item;
        ScrollToBottom();
    }

    #endregion

    #region Message List Helpers

    private void AddOrUpdateItem(GimBaseMessage msg, string source = "MessageList")
    {
        var key = msg.MessageId.ToString();
        if (_messageItems.TryGetValue(key, out var existing))
        {
            existing.Bind(msg, source);
        }
        else
        {
            var item = Instantiate(messageItemPrefab, messageListContent);
            item.Bind(msg, source);
            _messageItems[key] = item;
        }
    }

    private void PrependItem(GimBaseMessage msg)
    {
        var key = msg.MessageId.ToString();
        if (_messageItems.ContainsKey(key)) return;

        var item = Instantiate(messageItemPrefab, messageListContent);
        item.transform.SetAsFirstSibling();
        item.Bind(msg, "LoadPrevious");
        _messageItems[key] = item;
    }

    private void ClearMessageList()
    {
        foreach (Transform child in messageListContent)
            Destroy(child.gameObject);
        _messageItems.Clear();
    }

    private void RefreshLoadPreviousButton()
    {
        if (loadPreviousButton != null)
            loadPreviousButton.interactable = _messageCollection?.HasPrevious ?? false;
    }

    private void ScrollToBottom()
    {
        if (messageScrollRect != null)
            StartCoroutine(ScrollToBottomCoroutine());
    }

    private System.Collections.IEnumerator ScrollToBottomCoroutine()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        messageScrollRect.verticalNormalizedPosition = 0f;
    }

    #endregion
}
