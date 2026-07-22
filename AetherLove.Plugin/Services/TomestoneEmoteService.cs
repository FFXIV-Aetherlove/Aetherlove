using System;
using AetherLove.Config;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace AetherLove.Services;

/// <summary>Keeps a cosmetic emote looping while the phone is open in safe areas; stops re-arming (not force-cancel) when minimized.</summary>
public sealed unsafe class TomestoneEmoteService : IDisposable
{
    private const string TomestoneCommand = "/tomescroll";
    private const long StartThrottleMs = 1500;

    private readonly Configuration _config;
    private readonly ushort _emoteId;

    private Func<bool>? _phoneActive;
    private long _lastStartAttemptMs;
    private bool _running;

    public TomestoneEmoteService(Configuration config)
    {
        _config = config;
        _emoteId = ResolveEmoteId();
    }

    /// <summary><paramref name="phoneActive"/> reports whether the phone window is open AND focused.</summary>
    public void Start(Func<bool> phoneActive)
    {
        if (_running)
        {
            return;
        }
        _phoneActive = phoneActive;
        _running = true;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }
        _running = false;
        Plugin.Framework.Update -= OnFrameworkUpdate;
    }

    public void Dispose() => Stop();

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_emoteId == 0)
        {
            return;
        }
        // Only START (and keep re-arming) the pose while the phone is open, focused and in a safe area. Clicking
        // away / minimizing just stops re-starting it; the emote ends naturally when the player moves.
        if (!_config.OsSettings.ShowTomestoneEmote || _phoneActive?.Invoke() != true || !IsSafeNow())
        {
            return;
        }
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player is null || player.Address == nint.Zero)
            {
                return;
            }
            // Already in an emote loop (ours, or one the player chose): leave it running, never restart it.
            var character = (Character*)player.Address;
            if (character->Mode == CharacterModes.EmoteLoop)
            {
                return;
            }

            var now = Environment.TickCount64;
            if (now - _lastStartAttemptMs < StartThrottleMs)
            {
                return;
            }
            _lastStartAttemptMs = now;

            var agent = AgentEmote.Instance();
            if (agent != null && agent->CanUseEmote(_emoteId))
            {
                agent->ExecuteEmote(_emoteId, null, false, false);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[TomestoneEmote] tick failed.");
        }
    }

    private static bool IsSafeNow()
    {
        if (!Plugin.ClientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer is null)
        {
            return false;
        }
        var c = Plugin.Condition;
        return !c[ConditionFlag.InCombat]
            && !c[ConditionFlag.BetweenAreas]
            && !c[ConditionFlag.BetweenAreas51]
            && !c[ConditionFlag.WatchingCutscene]
            && !c[ConditionFlag.WatchingCutscene78]
            && !c[ConditionFlag.OccupiedInCutSceneEvent]
            && !c[ConditionFlag.OccupiedInQuestEvent]
            && !c[ConditionFlag.BoundByDuty]
            && !c[ConditionFlag.Mounted];
    }

    /// <summary>Resolves the /tomescroll (Read Tomestone) emote id from the game's data so no magic number is
    /// baked in; returns 0 when it can't be found, which disables the feature safely.</summary>
    private static ushort ResolveEmoteId()
    {
        try
        {
            foreach (var emote in Plugin.DataManager.GetExcelSheet<Emote>())
            {
                if (emote.TextCommand.ValueNullable is not { } tc)
                {
                    continue;
                }
                if (Matches(tc.Command) || Matches(tc.ShortCommand) || Matches(tc.Alias) || Matches(tc.ShortAlias))
                {
                    return (ushort)emote.RowId;
                }
            }
            Plugin.Log.Warning("[TomestoneEmote] could not find the /tomescroll emote; the setting will do nothing.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[TomestoneEmote] failed to resolve the emote id.");
        }
        return 0;

        static bool Matches(Lumina.Text.ReadOnly.ReadOnlySeString s) =>
            string.Equals(s.ExtractText(), TomestoneCommand, StringComparison.OrdinalIgnoreCase);
    }
}
