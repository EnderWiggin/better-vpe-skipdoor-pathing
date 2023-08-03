using System;
using HarmonyLib;
using TerrainMovement;
using Verse;
using Verse.AI;

namespace BetterVPESkipdoorPathing;

internal static class Compatibility
{
    public static void Init(Harmony harmony)
    {
        PatchTerrainMovementKit(harmony);
    }

    private static void PatchTerrainMovementKit(Harmony harmony)
    {
        //Compatibility with TerrainMovementKit mod - it completely replaces pathfinding, so we need different patch
        try
        {
            ((Action) (() =>
            {
                var original = AccessTools.Method(typeof(TerrainAwarePathFinder),
                    nameof(TerrainAwarePathFinder.FindPath), new[]
                    {
                        typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode),
                        typeof(PathFinderCostTuning)
                    });
                harmony.Patch(original,
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