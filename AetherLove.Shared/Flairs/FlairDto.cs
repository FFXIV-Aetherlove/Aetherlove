using System;
using MessagePack;

namespace AetherLove.Shared.Flairs;

/// <summary>A flair definition in the client-side catalog. Carries every language's text + description so the
/// client resolves to its own UI language locally; deck cards and profiles reference flairs by
/// <see cref="Id"/> only. A blank translation falls back to English.</summary>
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
