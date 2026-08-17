using System;
using MessagePack;

namespace AetherLove.Shared.Store;

/// <summary>A store category node; the client assembles the tree by ParentId. Blank translations fall
/// back to English, like flairs.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record StoreCategoryDto(
    Guid Id,
    Guid? ParentId,
    int SortOrder,
    string NameEnglish,
    string? NameSpanish,
    string? NameFrench,
    string? NameRussian,
    string? NameGerman,
    string? NamePortuguese,
    bool HasImage,
    uint AccentColor,
    int ProductCount,
    string? Icon = null,
    int ImageVersion = 0,
    string? Key = null);

/// <summary>One constituent of a bundle product, with enough denormalized identity to render the
/// contents list without a second fetch.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record StoreBundleItemDto(
    Guid ChildProductId,
    StoreItemKind ItemKind,
    string ItemRef,
    int Quantity,
    string NameEnglish,
    string? NameSpanish,
    string? NameFrench,
    string? NameRussian,
    string? NameGerman,
    string? NamePortuguese,
    bool ChildOwned,
    int ImageVersion = 0);

/// <summary>The palette a ThemePack unlocks, as 0xAARRGGBB. Mirrors the client's ThemeDefinition colors;
/// bezel geometry is deliberately not carried, purchased themes use the built-in defaults.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record StoreThemeColorsDto(
    uint Accent,
    uint AccentLight,
    uint AccentDark,
    uint ChipFill,
    uint SecondaryStart,
    uint SecondaryEnd,
    uint ButtonNormal,
    uint ButtonHovered,
    uint ButtonActive,
    uint? WindowControlColor,
    uint? HomeGlowColor);

/// <summary>How a theme fits itself to its frame art: the window's design width (the height is always 835),
/// the content insets, the status strip, the two window-control rects and the home button. Every
/// measurement is a design pixel measured from the window's top-left, which is the bezel image's top-left,
/// except BezelRight and BezelBottom, which are distances from the far edge. A theme without this block
/// borrows the built-in theme's measurements, which is what every pre-geometry purchase does.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record StoreThemeGeometryDto(
    float WindowWidth,
    float BezelTop,
    float BezelBottom,
    float BezelLeft,
    float BezelRight,
    float StatusBarTop,
    uint StatusBarTint,
    float StatusBarTimeAlign,
    float StatusBarRightInset,
    bool DrawWindowControls,
    float CloseButtonX,
    float CloseButtonY,
    float CloseButtonWidth,
    float CloseButtonHeight,
    float MinimizeButtonX,
    float MinimizeButtonY,
    float MinimizeButtonWidth,
    float MinimizeButtonHeight,
    StoreThemeHomeShape HomeShape,
    float HomeWidth,
    float HomeHeight,
    float HomeRounding,
    float HomePulseSeconds,
    float HomeCenterXOffset,
    float HomeCenterYOffset,
    float HomeHitWidth,
    float HomeHitHeight,
    uint? TourAccent);

/// <summary>A theme the caller owns, for the pickers. Delisted products stay listed: purchases are
/// permanent. A ThemePack with no colors configured never reaches this list.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record OwnedThemeDto(
    Guid ProductId,
    string NameEnglish,
    string? NameSpanish,
    string? NameFrench,
    string? NameRussian,
    string? NameGerman,
    string? NamePortuguese,
    StoreThemeColorsDto Colors,
    bool HasBezel,
    bool HasBackground,
    StoreThemeGeometryDto? Geometry = null);

/// <summary>The clean full-size assets of a theme, served only to an owner. The client seals these at
/// rest; they are never memoized server-side.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record StoreThemeAssetsDto(
    StoreThemeColorsDto Colors,
    byte[]? Bezel,
    byte[]? Background,
    StoreThemeGeometryDto? Geometry = null);

/// <summary>A sellable product. The server owns every number: DiscountPercent is the resolved effective
/// item discount and DiscountedPriceSparks the exact per-unit price checkout will charge the caller,
/// supporter discount included. OwnedQuantity is the caller's, so the DTO is per-caller in that one
/// field. BundleItems is empty for normal products; BundleWorthSparks is the sum of the children's
/// current effective prices for the savings line.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record StoreProductDto(
    Guid Id,
    StoreItemKind ItemKind,
    string ItemRef,
    Guid CategoryId,
    string NameEnglish,
    string? NameSpanish,
    string? NameFrench,
    string? NameRussian,
    string? NameGerman,
    string? NamePortuguese,
    string DescriptionEnglish,
    string? DescriptionSpanish,
    string? DescriptionFrench,
    string? DescriptionRussian,
    string? DescriptionGerman,
    string? DescriptionPortuguese,
    int PriceSparks,
    int DiscountPercent,
    int DiscountedPriceSparks,
    DateTimeOffset? DiscountEndsAtUtc,
    string[] Tags,
    int? MaxPerAccount,
    int OwnedQuantity,
    bool HasImage,
    uint AccentColor,
    DateTimeOffset CreatedAtUtc,
    long PurchaseCount,
    StoreBundleItemDto[] BundleItems,
    int BundleWorthSparks,
    StoreThemeColorsDto? ThemeColors = null,
    bool HasBackground = false,
    int ImageVersion = 0,
    int BackgroundVersion = 0);

/// <summary>An active or upcoming sale window; EndsAtUtc is the real end time the client counts down to.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record StoreSaleBannerDto(
    Guid Id,
    int Percent,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string NameEnglish,
    string? NameSpanish,
    string? NameFrench,
    string? NameRussian,
    string? NameGerman,
    string? NamePortuguese);

/// <summary>An editorial shelf: an eyebrow line, a headline, a picture and a handful of picks.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record StoreCollectionDto(
    Guid Id,
    string Key,
    string TitleEnglish,
    string? TitleSpanish,
    string? TitleFrench,
    string? TitleRussian,
    string? TitleGerman,
    string? TitlePortuguese,
    string SubtitleEnglish,
    string? SubtitleSpanish,
    string? SubtitleFrench,
    string? SubtitleRussian,
    string? SubtitleGerman,
    string? SubtitlePortuguese,
    bool HasImage,
    uint AccentColor,
    StoreProductDto[] Products,
    int ImageVersion = 0);

/// <summary>The storefront in one fetch: the category tree, live sales, and the curated rails.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record StoreFrontDto(
    StoreCategoryDto[] Categories,
    StoreSaleBannerDto[] ActiveSales,
    StoreProductDto[] NewItems,
    StoreProductDto[] MostBought,
    long Balance,
    StoreProductDto[] Bundles,
    StoreCollectionDto[] Collections,
    int SupporterDiscountPercent = 0);

/// <summary>Browse filter; the category filter includes descendants and the price bounds apply to the
/// effective (discounted) price.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record StoreProductQueryDto(
    Guid? CategoryId,
    string? Tag,
    string? SearchText,
    int? MinPriceSparks,
    int? MaxPriceSparks,
    bool OnSaleOnly,
    int Skip,
    int Take,
    StoreSort Sort = StoreSort.Featured);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record StoreProductPageDto(StoreProductDto[] Items, int TotalCount);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record StorePurchaseResultDto(
    Guid PurchaseId,
    int TotalPaidSparks,
    long NewBalance,
    int OwnedQuantity);

/// <summary>One row of the caller's (currently hidden) inventory.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record StoreInventoryItemDto(
    StoreItemKind ItemKind,
    string ItemRef,
    Guid ProductId,
    int Quantity,
    DateTimeOffset FirstAcquiredAtUtc);
