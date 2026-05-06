using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Gamania.GIMChat;

/// <summary>
/// One message row in ChatPage.
/// Click to toggle meta panel.
/// </summary>
public class MessageItemController : MonoBehaviour
{
    [Header("Main Row")]
    [SerializeField] private TextMeshProUGUI senderText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private TextMeshProUGUI statusBadge;
    [SerializeField] private Button toggleButton;

    [Header("Meta Panel (hidden by default)")]
    [SerializeField] private GameObject metaPanel;
    [SerializeField] private TextMeshProUGUI metaMessageId;
    [SerializeField] private TextMeshProUGUI metaCustomType;
    [SerializeField] private TextMeshProUGUI metaDone;
    [SerializeField] private TextMeshProUGUI metaSource;
    [SerializeField] private TextMeshProUGUI metaCreatedAt;

    private bool _metaVisible;

    private void Awake()
    {
        toggleButton.onClick.AddListener(ToggleMeta);
        metaPanel.SetActive(false);
    }

    /// <summary>
    /// Bind a confirmed message (from collection or send ACK).
    /// </summary>
    public void Bind(GimBaseMessage msg, string source = "-")
    {
        var sender = string.IsNullOrEmpty(msg.Sender?.Nickname)
            ? (msg.Sender?.UserId ?? "Unknown")
            : msg.Sender.Nickname;

        senderText.text = sender;
        messageText.text = msg.Message;

        if (msg is GimUserMessage userMsg)
        {
            SetStatus(userMsg.SendingStatus);
        }
        else
        {
            statusBadge.gameObject.SetActive(false);
        }

        metaSource.text = $"Source: {source}";
        metaMessageId.text = $"ID: {msg.MessageId}";
        metaCustomType.text = $"CustomType: {(string.IsNullOrEmpty(msg.CustomType) ? "-" : msg.CustomType)}";
        metaDone.text = $"Done: {msg.Done}";
        metaCreatedAt.text = $"CreatedAt: {System.DateTimeOffset.FromUnixTimeMilliseconds(msg.CreatedAt):yyyy-MM-dd HH:mm:ss}";

        // Refresh meta panel if already open
        if (_metaVisible)
            metaPanel.SetActive(true);
    }

    /// <summary>
    /// Bind a pending (not-yet-confirmed) message.
    /// </summary>
    public void BindPending(string reqId, string userId, string text)
    {
        senderText.text = userId;
        messageText.text = text;
        SetStatus(GimSendingStatus.Pending);

        metaMessageId.text = $"ReqId: {reqId}";
        metaCustomType.text = "CustomType: -";
        metaDone.text = "Done: -";
        metaSource.text = "Source: pending";
        metaCreatedAt.text = $"CreatedAt: {System.DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}";
    }

    public void SetStatus(GimSendingStatus status)
    {
        if (statusBadge == null) return;
        statusBadge.gameObject.SetActive(true);
        statusBadge.text = status switch
        {
            GimSendingStatus.Pending => "Sending",
            GimSendingStatus.Succeeded => "Sent",
            GimSendingStatus.Failed => "Failed",
            _ => ""
        };
        statusBadge.color = status switch
        {
            GimSendingStatus.Pending => new Color(1f, 0.7f, 0.2f),   // amber
            GimSendingStatus.Succeeded => new Color(0.2f, 0.8f, 0.4f), // green
            GimSendingStatus.Failed => new Color(0.9f, 0.3f, 0.3f),    // red
            _ => Color.white
        };
    }

    public void MarkDeleted()
    {
        messageText.text = "[Deleted]";
        messageText.color = new Color(0.5f, 0.5f, 0.5f);
        if (statusBadge != null) statusBadge.gameObject.SetActive(false);
    }

    private void ToggleMeta()
    {
        _metaVisible = !_metaVisible;
        metaPanel.SetActive(_metaVisible);
    }
}
