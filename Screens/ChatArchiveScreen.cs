namespace AetherLove.Screens;

/// <summary>The archived-chats view. Shares ChatListScreen's data and rendering, filtered to archived matches.</summary>
public sealed class ChatArchiveScreen
{
    private readonly ChatListScreen _chatList;

    public ChatArchiveScreen(ChatListScreen chatList) => _chatList = chatList;

    public void OnShow() => _chatList.OnShow();
    public void OnHide() => _chatList.OnHide();
    public void Draw() => _chatList.DrawArchiveView();
}
