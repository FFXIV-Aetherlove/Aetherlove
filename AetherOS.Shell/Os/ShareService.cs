using System.Collections.Generic;
using System.Linq;
using AetherOS.Sdk;

namespace AetherLove.Os;

/// <summary>The share source API: discovers which apps accept a <see cref="ShareItem"/>'s type, shows the OS
/// share sheet, and delivers the pick by wrapping the item in one reserved <see cref="ShareIntent"/> over the
/// existing <see cref="OsShell.SendIntent"/> path. A source never names a target.</summary>
public sealed class ShareService : IShareService
{
    private readonly OsShell _shell;
    private readonly ShareSheet _sheet;

    public ShareService(OsShell shell, ShareSheet sheet)
    {
        _shell = shell;
        _sheet = sheet;
    }

    public IReadOnlyList<IAetherApp> TargetsFor(string type) =>
        _shell.Apps.Where(a => Offers(a) && a.AcceptedShareTypes.Contains(type)).ToList();

    private bool Offers(IAetherApp app) => app.Available && !_shell.IsAppRemoved(app.Id);

    public void Offer(ShareItem item, string? title = null)
    {
        var targets = _shell.Apps
            .Where(a => Offers(a) && a.AcceptedShareTypes.Contains(item.Type) && a.Id != item.SourceAppId)
            .ToList();
        if (targets.Count == 0)
        {
            return;
        }
        _sheet.Open(targets, item, title, chosen => _shell.SendIntent(chosen.Id, ShareIntent.Wrap(item)));
    }
}
