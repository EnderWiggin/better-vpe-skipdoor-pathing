using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using Verse;

namespace BetterVPESkipdoorPathing;

[UsedImplicitly]
public class ModEntry : Mod
{
    public ModEntry(ModContentPack content) : base(content)
    {
        new Harmony(Content.PackageIdPlayerFacing).PatchAll();
        GetSettings<Settings>();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var options = new Listing_Standard();
        options.Begin(inRect);
        options.Label("BetterVPESkipdoorPathing.Settings.Allowed".Translate());
        MakeAllowedRadioButton(options, Allowed.Everyone);
        MakeAllowedRadioButton(options, Allowed.Friendlies);
        MakeAllowedRadioButton(options, Allowed.Colonists);
        options.End();
        
        base.DoSettingsWindowContents(inRect);
    }
    
    private static void MakeAllowedRadioButton(Listing_Standard opt, Allowed type)
    {
        if (opt.RadioButton($"BetterVPESkipdoorPathing.Settings.Allowed.{type}.Name".Translate(), 
                Settings.Allowed == type, 20, 
                $"BetterVPESkipdoorPathing.Settings.Allowed.{type}.Tip".Translate()))
        {
            Settings.Allowed = type;
        }
    }

    public override string SettingsCategory()
    {
        return "BetterVPESkipdoorPathing.Settings".Translate();
    }
}