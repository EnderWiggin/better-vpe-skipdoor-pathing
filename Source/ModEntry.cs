using System;
using HarmonyLib;
using JetBrains.Annotations;
using TerrainMovement;
using UnityEngine;
using Verse;
using Verse.AI;

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

[UsedImplicitly]
[StaticConstructorOnStartup]
public static class Setup
{
    static Setup()
    {
        var harmony = new Harmony("BetterVPESkipdoorPathing");
        PatchTerrainMovementKit(harmony);
    }

    private static void PatchTerrainMovementKit(Harmony harmony)
    {
        //Compatibility with TerrainMovementKit mod - it completely replaces pathfinding, so we need different patch
        try
        {
            ((Action) (() =>
            {
                harmony.Patch(AccessTools.Method(typeof(TerrainAwarePathFinder),
                        nameof(TerrainAwarePathFinder.FindPath), new[]
                        {
                            typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode),
                            typeof(PathFinderCostTuning)
                        }),
                    transpiler: new HarmonyMethod(AccessTools.Method(
                        typeof(TerrainMovement_TerrainAwarePathFinder_FindPath_Patch),
                        nameof(TerrainMovement_TerrainAwarePathFinder_FindPath_Patch.Transpiler))));
            }))();
        }
        catch (TypeLoadException)
        {
        }
    }
}