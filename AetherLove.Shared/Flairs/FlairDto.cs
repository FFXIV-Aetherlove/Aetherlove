using System;
using MessagePack;

namespace AetherLove.Shared.Flairs;

/// <summary>Client-side flair with multi-language text; blank translation falls back to English. Referenced by Id only.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record FlairDto(
    Guid Id,
    string BackgroundColor,
    string TextEnglish,
    string? TextSpanish,
    string? TextFrench,
    string? TextRussian,
    string? TextGerman,
    string? TextPortuguese,
    string DescriptionEnglish,
    string? DescriptionSpanish,
    string? DescriptionFrench,
    string? DescriptionRussian,
    string? DescriptionGerman,
    string? DescriptionPortuguese,
    string Key = "");

/// <summary>Well-known flair keys the client attaches behavior to.</summary>
public static class FlairKeys
{
    /// <summary>The supporter badge flair, appended server-side while a profile is a supporter.</summary>
    public const string Supporter = "supporter";
}
