using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherOS.Sdk;

/// <summary>What a translation produced. <see cref="SourceLanguage"/> is the detected source code
/// ("ja"), when the backend reported one.</summary>
public sealed record TranslationResult(string Text, string? SourceLanguage);

/// <summary>Per-message text translation for any app surface. Client-direct to the translation backend
/// by decision (ADR 9): the text leaves the machine toward a third party, which is why everything is
/// gated behind <see cref="Enabled"/>, an explicit opt-in the user gives once through a consent
/// explainer or the settings app.</summary>
public interface ITranslationBridge
{
    /// <summary>Whether the user has opted in. While false, surfaces show the consent popup instead of
    /// translating.</summary>
    bool Enabled { get; }

    /// <summary>The target language code ("en", "de", "ja").</summary>
    string TargetLanguage { get; }

    /// <summary>The user accepted the consent explainer: persists the opt-in.</summary>
    void Enable();

    /// <summary>Translates into <see cref="TargetLanguage"/> with source auto-detection. Null on any
    /// failure (offline, throttled, unparseable); callers show a soft "didn't work" state, never an
    /// error dialog.</summary>
    Task<TranslationResult?> TranslateAsync(string text, CancellationToken ct = default);
}
