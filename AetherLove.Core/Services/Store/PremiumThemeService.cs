using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Crypto;
using AetherLove.Services.Hub;
using AetherLove.Shared.Store;
using AetherLove.UI;
using Dalamud.Interface.Textures.TextureWraps;
using MessagePack;

namespace AetherLove.Services.Store;

/// <summary>A purchased theme's assets as they live on disk: sealed, so the file is inert on any other
/// install and unreadable by anything that is not this plugin holding this account's keys.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SealedTheme(
    string Name,
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
    uint? HomeGlowColor,
    byte[]? Bezel,
    byte[]? Background,
    StoreThemeGeometryDto? Geometry = null);

/// <summary>Owns purchased phone themes client-side: fetches the clean assets once, seals them at rest
/// (AES-GCM, key derived from the account keypair and the product id), and activates a theme by decrypting
/// into memory. Nothing is ever written unencrypted and no decrypted byte reaches a file, so a seal shared
/// with someone else is noise. A seal that will not open is deleted and refetched; that is the recovery
/// path after a passphrase reset rotates the keys.</summary>
public sealed class PremiumThemeService : IDisposable
{
    private const int NonceLength = 12;
    private const string HomeTooltipKey = "os.home";

    private readonly AetherHubContext _hub;
    private readonly CryptoService _crypto;
    private readonly KeyStorageService _keys;
    private readonly Config.Configuration _config;
    private readonly ConcurrentDictionary<Guid, Loaded> _loaded = new();
    private readonly ConcurrentBag<(DateTime At, IDalamudTextureWrap Wrap)> _retired = [];

    private sealed class Loaded
    {
        public required SealedTheme Theme { get; init; }
        public IDalamudTextureWrap? Bezel { get; set; }
        public IDalamudTextureWrap? Background { get; set; }
        public bool BezelRequested { get; set; }
        public bool BackgroundRequested { get; set; }
    }

    public PremiumThemeService(
        AetherHubContext hub, CryptoService crypto, KeyStorageService keys, Config.Configuration config)
    {
        _hub = hub;
        _crypto = crypto;
        _keys = keys;
        _config = config;
    }

    private static string SkinDir =>
        Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "skins");

    private static string SealPath(Guid productId) => Path.Combine(SkinDir, $"{productId:N}.aeskin");

    /// <summary>The themes this install already holds sealed, so a picker can show them offline.</summary>
    public IReadOnlyList<Guid> SealedIds
    {
        get
        {
            try
            {
                if (!Directory.Exists(SkinDir))
                {
                    return [];
                }
                return Directory.EnumerateFiles(SkinDir, "*.aeskin")
                    .Select(f => Guid.TryParseExact(Path.GetFileNameWithoutExtension(f), "N", out var id) ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty)
                    .ToList();
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[PremiumTheme] Could not enumerate the sealed themes.");
                return [];
            }
        }
    }

    /// <summary>Activates a theme, fetching and sealing its assets first when this install has none.</summary>
    public async Task<bool> EnableAsync(Guid productId, CancellationToken ct = default)
    {
        if (Load(productId) is null && !await DownloadAndSealAsync(productId, ct).ConfigureAwait(false))
        {
            return false;
        }
        return Activate(productId);
    }

    /// <summary>Throws away what this install holds for a theme and pulls it again, so an author who has
    /// just corrected the geometry or the palette in admin sees it without reinstalling. Re-activates when
    /// the theme is the one currently worn.</summary>
    public async Task<bool> RefreshAsync(Guid productId, CancellationToken ct = default)
    {
        DrainRetired();
        if (_loaded.TryRemove(productId, out var stale))
        {
            Retire(stale.Bezel);
            Retire(stale.Background);
        }
        TryDelete(SealPath(productId));
        if (!await DownloadAndSealAsync(productId, ct).ConfigureAwait(false))
        {
            return false;
        }
        return ThemeService.PremiumThemeId != productId || Activate(productId);
    }

    /// <summary>Pulls the clean assets from the server and writes the seal. Fails quietly when the account
    /// keypair is not on this device yet, which is a soft retry rather than an error.</summary>
    public async Task<bool> DownloadAndSealAsync(Guid productId, CancellationToken ct = default)
    {
        if (_keys.AccountKeys is not { } accountKeys)
        {
            return false;
        }
        try
        {
            var assets = await _hub.GetStoreThemeAssetsAsync(productId, ct).ConfigureAwait(false);
            if (assets is null)
            {
                return false;
            }
            var owned = await _hub.GetMyStoreThemesAsync(ct).ConfigureAwait(false);
            var name = owned.FirstOrDefault(t => t.ProductId == productId)?.NameEnglish ?? "Premium theme";
            var payload = new SealedTheme(
                name,
                assets.Colors.Accent, assets.Colors.AccentLight, assets.Colors.AccentDark, assets.Colors.ChipFill,
                assets.Colors.SecondaryStart, assets.Colors.SecondaryEnd,
                assets.Colors.ButtonNormal, assets.Colors.ButtonHovered, assets.Colors.ButtonActive,
                assets.Colors.WindowControlColor, assets.Colors.HomeGlowColor,
                assets.Bezel, assets.Background, assets.Geometry);
            Seal(productId, payload, accountKeys.PrivateKey);
            _loaded.TryRemove(productId, out _);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[PremiumTheme] Could not fetch theme {Id}.", productId);
            return false;
        }
    }

    /// <summary>Switches the phone to a sealed theme. Returns false when the seal is missing or will not
    /// open, in which case it is deleted so the next enable refetches.</summary>
    public bool Activate(Guid productId)
    {
        if (Load(productId) is not { } loaded)
        {
            return false;
        }
        var theme = loaded.Theme;
        var g = theme.Geometry;
        var fallback = ThemeService.Themes.TryGetValue(ThemeService.CurrentTheme, out var f)
            ? f
            : ThemeService.Themes[AppTheme.CrystalVoid];
        var definition = new ThemeDefinition
        {
            Name = theme.Name,
            // The built-in frame shows until the sealed one has decoded, so the phone is never frameless.
            BackgroundImageFile = fallback.BackgroundImageFile,
            BezelTexture = () => BezelWrap(productId),
            Accent = Rgba(theme.Accent),
            AccentLight = Rgba(theme.AccentLight),
            AccentDark = Rgba(theme.AccentDark),
            ChipFill = Rgba(theme.ChipFill),
            SecondaryStart = Rgba(theme.SecondaryStart),
            SecondaryEnd = Rgba(theme.SecondaryEnd),
            ButtonNormal = Rgba(theme.ButtonNormal),
            ButtonHovered = Rgba(theme.ButtonHovered),
            ButtonActive = Rgba(theme.ButtonActive),
            WindowControlColor = theme.WindowControlColor is { } wc ? Rgba(wc) : null,
            // The theme's own frame measurements when it was authored with them, else the built-in's. Every
            // field has to come from the same source: the class defaults put the minimize rect at y 0, which
            // trips the status bar's auto-clamp and pins the battery to 360px whatever the frame looks like.
            WindowWidth = g?.WindowWidth ?? fallback.WindowWidth,
            BezelTop = g?.BezelTop ?? fallback.BezelTop,
            BezelBottom = g?.BezelBottom ?? fallback.BezelBottom,
            BezelLeft = g?.BezelLeft ?? fallback.BezelLeft,
            BezelRight = g?.BezelRight ?? fallback.BezelRight,
            StatusBarTop = g?.StatusBarTop ?? fallback.StatusBarTop,
            StatusBarTint = g is null ? fallback.StatusBarTint : Rgba(g.StatusBarTint),
            StatusBarTimeAlign = g?.StatusBarTimeAlign ?? fallback.StatusBarTimeAlign,
            StatusBarRightInset = g?.StatusBarRightInset ?? fallback.StatusBarRightInset,
            MinimizeButtonTL = g is null
                ? fallback.MinimizeButtonTL
                : new Vector2(g.MinimizeButtonX, g.MinimizeButtonY),
            MinimizeButtonSize = g is null
                ? fallback.MinimizeButtonSize
                : new Vector2(g.MinimizeButtonWidth, g.MinimizeButtonHeight),
            CloseButtonTL = g is null ? fallback.CloseButtonTL : new Vector2(g.CloseButtonX, g.CloseButtonY),
            CloseButtonSize = g is null
                ? fallback.CloseButtonSize
                : new Vector2(g.CloseButtonWidth, g.CloseButtonHeight),
            DrawWindowControls = g?.DrawWindowControls ?? fallback.DrawWindowControls,
            TourAccent = g?.TourAccent is { } tour ? Rgba(tour) : fallback.TourAccent,
            HomeButton = g is null ? fallback.HomeButton : BuildHomeButton(g, theme.HomeGlowColor),
        };
        ThemeService.SetPremiumTheme(productId, definition);
        if (theme.Background is { Length: > 0 })
        {
            _config.Os.WallpaperMode = AetherOS.Sdk.WallpaperMode.Premium;
            _config.Os.PremiumWallpaperProductId = productId;
            _config.Save();
        }
        return true;
    }

    /// <summary>The theme's wallpaper, for the shell's premium wallpaper mode. Null while it is loading or
    /// when the theme ships without one.</summary>
    public IDalamudTextureWrap? BackgroundWrap(Guid productId)
    {
        if (Load(productId) is not { } loaded || loaded.Theme.Background is not { Length: > 0 } bytes)
        {
            return null;
        }
        if (!loaded.BackgroundRequested)
        {
            loaded.BackgroundRequested = true;
            CreateTexture(bytes, wrap => loaded.Background = wrap);
        }
        return loaded.Background;
    }

    /// <summary>Restores the theme the user last chose. Runs at boot from the plugin's bootstrap, never
    /// from ThemeService.Initialise, which is constructed before any service exists.</summary>
    public void TryRestoreOnBoot()
    {
        if (_config.SelectedPremiumThemeId is not { } productId)
        {
            return;
        }
        if (Activate(productId))
        {
            return;
        }
        // The seal is gone or was written by another account on this install; heal once the hub is up.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                if (await DownloadAndSealAsync(productId).ConfigureAwait(false))
                {
                    Activate(productId);
                }
                else if (_keys.AccountKeys is not null)
                {
                    ThemeService.ClearPremiumTheme();
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[PremiumTheme] Boot restore of {Id} failed.", productId);
            }
        });
    }

    private static HomeButtonRenderer BuildHomeButton(StoreThemeGeometryDto g, uint? glow)
    {
        var color = glow is { } value ? Rgba(value) : new Vector4(0.25f, 0.62f, 1f, 1f);
        var hit = new Vector2(g.HomeHitWidth, g.HomeHitHeight);
        return g.HomeShape switch
        {
            StoreThemeHomeShape.GoldenPill => new GoldenPillHomeButton
            {
                GoldColor = color,
                Width = g.HomeWidth,
                Height = g.HomeHeight,
                Rounding = g.HomeRounding,
                PulseSeconds = g.HomePulseSeconds,
                CenterXOffset = g.HomeCenterXOffset,
                CenterYOffset = g.HomeCenterYOffset,
                HitSize = hit,
                TooltipKey = HomeTooltipKey,
            },
            StoreThemeHomeShape.Pill => new PillHomeButton
            {
                CenterXOffset = g.HomeCenterXOffset,
                CenterYOffset = g.HomeCenterYOffset,
                HitSize = hit,
                TooltipKey = HomeTooltipKey,
            },
            _ => new NeonSquareHomeButton
            {
                GlowColor = color,
                Size = g.HomeWidth,
                Rounding = g.HomeRounding,
                PulseSeconds = g.HomePulseSeconds,
                CenterXOffset = g.HomeCenterXOffset,
                CenterYOffset = g.HomeCenterYOffset,
                HitSize = hit,
                TooltipKey = HomeTooltipKey,
            },
        };
    }

    private IDalamudTextureWrap? BezelWrap(Guid productId)
    {
        if (Load(productId) is not { } loaded || loaded.Theme.Bezel is not { Length: > 0 } bytes)
        {
            return null;
        }
        if (!loaded.BezelRequested)
        {
            loaded.BezelRequested = true;
            CreateTexture(bytes, wrap => loaded.Bezel = wrap);
        }
        return loaded.Bezel;
    }

    private Loaded? Load(Guid productId)
    {
        if (_loaded.TryGetValue(productId, out var cached))
        {
            return cached;
        }
        if (_keys.AccountKeys is not { } accountKeys)
        {
            return null;
        }
        var path = SealPath(productId);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var blob = File.ReadAllBytes(path);
            if (blob.Length <= NonceLength)
            {
                File.Delete(path);
                return null;
            }
            var nonce = blob[..NonceLength];
            var body = blob[NonceLength..];
            var key = _crypto.DeriveSkinKey(accountKeys.PrivateKey, productId);
            var plain = _crypto.Decrypt(key, nonce, body);
            var loaded = new Loaded { Theme = MessagePackSerializer.Deserialize<SealedTheme>(plain) };
            _loaded[productId] = loaded;
            return loaded;
        }
        catch (Exception ex)
        {
            // A seal this install cannot open is worthless: drop it so the next enable refetches.
            UiHost.Log.Warning(ex, "[PremiumTheme] Discarding an unreadable seal for {Id}.", productId);
            TryDelete(path);
            return null;
        }
    }

    private void Seal(Guid productId, SealedTheme payload, byte[] accountPrivateKey)
    {
        Directory.CreateDirectory(SkinDir);
        var key = _crypto.DeriveSkinKey(accountPrivateKey, productId);
        var (ciphertext, nonce) = _crypto.Encrypt(key, MessagePackSerializer.Serialize(payload));
        var blob = new byte[nonce.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, blob, nonce.Length, ciphertext.Length);
        File.WriteAllBytes(SealPath(productId), blob);
    }

    private static void CreateTexture(byte[] bytes, Action<IDalamudTextureWrap> onReady)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                onReady(await UiHost.TextureProvider.CreateFromImageAsync(bytes).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[PremiumTheme] Could not decode a sealed image.");
            }
        });
    }

    /// <summary>A replaced texture can still be referenced by the frame ImGui is mid-way through building,
    /// so it is parked rather than disposed and only freed once no live draw list can hold it.</summary>
    private void Retire(IDalamudTextureWrap? wrap)
    {
        if (wrap is not null)
        {
            _retired.Add((DateTime.UtcNow, wrap));
        }
    }

    private void DrainRetired()
    {
        var keep = new List<(DateTime At, IDalamudTextureWrap Wrap)>();
        while (_retired.TryTake(out var entry))
        {
            if (DateTime.UtcNow - entry.At > TimeSpan.FromSeconds(5))
            {
                entry.Wrap.Dispose();
            }
            else
            {
                keep.Add(entry);
            }
        }
        foreach (var entry in keep)
        {
            _retired.Add(entry);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[PremiumTheme] Could not delete a seal.");
        }
    }

    private static Vector4 Rgba(uint argb) => new(
        ((argb >> 16) & 0xFF) / 255f,
        ((argb >> 8) & 0xFF) / 255f,
        (argb & 0xFF) / 255f,
        ((argb >> 24) & 0xFF) / 255f);

    public void Dispose()
    {
        foreach (var loaded in _loaded.Values)
        {
            loaded.Bezel?.Dispose();
            loaded.Background?.Dispose();
        }
        _loaded.Clear();
        while (_retired.TryTake(out var entry))
        {
            entry.Wrap.Dispose();
        }
    }
}
