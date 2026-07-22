namespace AetherLove.Screens;

/// <summary>The inside-a-category view. Shares ChatListScreen's data and rendering, filtered to the open category.</summary>
public sealed class ChatCategoryScreen
{
    private readonly ChatListScreen _chatList;

    public ChatCategoryScreen(ChatListScreen chatList) => _chatList = chatList;

    public void OnShow() => _chatList.OnShow();
    public void OnHide() => _chatList.OnHide();
    public void Draw() => _chatList.DrawCategoryView();
}
