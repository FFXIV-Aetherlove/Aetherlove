namespace AetherLove.Shared.News;

public enum NewsStatus : short
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

public enum NewsLineKind : short
{
    Text = 0,
    Image = 1,
    Heading = 2,
    Card = 3,
    Button = 4,
    Divider = 5,
}
