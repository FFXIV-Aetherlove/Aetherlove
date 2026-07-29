using System.Collections.Generic;
using System.Linq;
using AetherLove.Services.Realtor;
using AetherOS.Sdk;

namespace AetherOS.Apps.Realtor;

/// <summary>The app-wide plot filters (sizes plus FC/personal), set anywhere and honored everywhere.
/// Persisted so the hunt survives closing the phone.</summary>
internal sealed class RealtorFilters
{
    private readonly IAppStorage _storage;

    public HashSet<int> Sizes { get; } = [];
    public bool FreeCompany { get; private set; }
    public bool Personal { get; private set; }

    public RealtorFilters(IAppStorage storage)
    {
        _storage = storage;
        var mask = storage.Get<int?>("filterSizes") ?? 0;
        for (var size = 0; size <= 2; size++)
        {
            if ((mask & (1 << size)) != 0)
            {
                Sizes.Add(size);
            }
        }
        FreeCompany = storage.Get<bool?>("filterFc") ?? false;
        Personal = storage.Get<bool?>("filterPersonal") ?? false;
    }

    public bool Active => Sizes.Count > 0 || FreeCompany || Personal;

    public void ToggleSize(int size)
    {
        if (!Sizes.Add(size))
        {
            Sizes.Remove(size);
        }
        Persist();
    }

    public void ToggleFreeCompany()
    {
        FreeCompany = !FreeCompany;
        Persist();
    }

    public void TogglePersonal()
    {
        Personal = !Personal;
        Persist();
    }

    public bool Matches(PaissaPlot plot)
    {
        if (Sizes.Count > 0 && !Sizes.Contains(plot.Size))
        {
            return false;
        }
        if ((FreeCompany || Personal)
            && !(FreeCompany && plot.AllowsFreeCompany) && !(Personal && plot.AllowsIndividual))
        {
            return false;
        }
        return true;
    }

    public int CountFor(PaissaDistrict district)
    {
        if (!Active)
        {
            return district.NumOpenPlots;
        }
        return district.OpenPlots?.Count(Matches) ?? 0;
    }

    private void Persist()
    {
        var mask = 0;
        foreach (var size in Sizes)
        {
            mask |= 1 << size;
        }
        _storage.Set("filterSizes", (int?)mask);
        _storage.Set("filterFc", (bool?)FreeCompany);
        _storage.Set("filterPersonal", (bool?)Personal);
    }
}
