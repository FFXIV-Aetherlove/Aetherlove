using System;
using AetherLove.Shared.Profile.Enums;
using MessagePack;

namespace AetherLove.Shared.News;

/// <summary>Lightweight news entry shipped at connection time and in the published-push, enough to badge/list
/// without the (potentially heavy) body. Full content is fetched on open via the news detail call.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record NewsSummaryDto(
    Guid Id,
    string Title,
    Language Language,
    DateTimeOffset PublishedAtUtc,
    string Preview = "");

/// <summary>One body line — flat union (the MessagePack contractless resolver doesn't do polymorphic [Union]).
/// Text lines carry <see cref="Text"/> (with <c>:emoji:</c> shortcodes); image lines carry client-ready
/// <see cref="ImageBytes"/> plus their pixel dimensions.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record NewsLineDto(
    NewsLineKind Kind,
    string? Text,
    byte[]? ImageBytes,
    int? Width,
    int? Height);

/// <summary>Full news entry with its rendered body, fetched when the user opens an entry.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record NewsDto(
    Guid Id,
    string Title,
    Language Language,
    NewsStatus Status,
    DateTimeOffset? PublishedAtUtc,
    NewsLineDto[] Lines);

/// <summary>A compact card for a chat news share (<c>[news=guid]</c>). Like the venue card it is fetched live
/// by the receiving client, so the server never stores that a card was shared and it always reflects the
/// current published entry (missing / unpublished entries fetch as null and render a tombstone).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record NewsCardDto(
    Guid Id,
    string Title,
    string Preview,
    DateTimeOffset? PublishedAtUtc);
