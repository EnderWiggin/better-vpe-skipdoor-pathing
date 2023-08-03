using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using TerrainMovement;
using Verse;

namespace BetterVPESkipdoorPathing;

static class TerrainMovement_TerrainAwarePathFinder_FindPath_Patch
{
    [UsedImplicitly]
    static void Postfix()
    {
        TerrainAwarePathfindingUtils.ResetParams();
    }

    [UsedImplicitly]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        CodeInstruction loadCurIndex = null;
        CodeInstruction loadUsedHeuristics = null;
        CodeInstruction loadX2 = null;
        CodeInstruction loadIndex4 = null;

        var fld_parentIndex = AccessTools.Field(typeof(TerrainAwarePathFinder.PathFinderNodeFast),
            nameof(TerrainAwarePathFinder.PathFinderNodeFast.parentIndex));
        var mtd_FinalizedPath =
            AccessTools.Method(typeof(TerrainAwarePathFinder), nameof(TerrainAwarePathFinder.FinalizedPath));
        var mtd_OctileDistance = AccessTools.Method(typeof(GenMath), nameof(GenMath.OctileDistance));
        var fld_costNodeCost = AccessTools.Field(typeof(TerrainAwarePathFinder.PathFinderNodeFast),
            nameof(TerrainAwarePathFinder.PathFinderNodeFast.costNodeCost));
        var fld_status = AccessTools.Field(typeof(TerrainAwarePathFinder.PathFinderNodeFast),
            nameof(TerrainAwarePathFinder.PathFinderNodeFast.status));
        var fld_calcGrid = AccessTools.Field(typeof(TerrainAwarePathFinder), nameof(TerrainAwarePathFinder.calcGrid));
        var fld_statusClosedValue = AccessTools.Field(typeof(TerrainAwarePathFinder),
            nameof(TerrainAwarePathFinder.statusClosedValue));

        var insertIndex = -1;
        var octileIndex = -1;

        var codes = instructions.ToList();

        for (var i = 1; i < codes.Count - 1; i++)
        {
            var prev = codes[i - 1];
            var code = codes[i];
            var next = codes[i + 1];

            //curIndex - ldloc.3      // curIndex
            //           stfld        int32 Verse.AI.PathFinder/PathFinderNodeFast::parentIndex
            if (loadCurIndex == null && code.StoresField(fld_parentIndex) && prev.IsLdloc())
            {
                loadCurIndex = prev.Clone();
            }

            //usedRegionHeuristics - ldloc.s      usedRegionHeuristics
            //                       call         instance class Verse.AI.PawnPath Verse.AI.PathFinder::FinalizedPath(int32, bool)
            if (loadUsedHeuristics == null && code.Calls(mtd_FinalizedPath) && prev.IsLdloc())
            {
                loadUsedHeuristics = prev.Clone();
            }

            //x2 - stfld        int32 Verse.AI.PathFinder/PathFinderNodeFast::costNodeCost
            //     ldloc.s      x2
            if (loadX2 == null && code.StoresField(fld_costNodeCost) && next.IsLdloc())
            {
                loadX2 = new CodeInstruction(OpCodes.Ldloca_S, next.operand);
            }

            // add our code before this
            // ldsfld       valuetype Verse.AI.PathFinder/PathFinderNodeFast[] Verse.AI.PathFinder::calcGrid
            // ldloc.3      // curIndex
            // ldelema      Verse.AI.PathFinder/PathFinderNodeFast
            // ldsfld       unsigned int16 Verse.AI.PathFinder::statusClosedValue
            // stfld        unsigned int16 Verse.AI.PathFinder/PathFinderNodeFast::status
            if (code.LoadsField(fld_calcGrid)
                && codes.Count - i > 5
                && codes[i + 1].IsLdloc()
                && codes[i + 3].LoadsField(fld_statusClosedValue)
                && codes[i + 4].StoresField(fld_status))
            {
                insertIndex = i;
            }

            if (code.Calls(mtd_OctileDistance))
            {
                octileIndex = i;
                loadIndex4 = new CodeInstruction(codes[i + 3]);
            }
        }

        if (insertIndex < 0)
        {
            Log.Error("[Better VPE Skipdoor pathing TMKCP] couldn't find insert position");
        }
        else
        {
            var method = AccessTools.Method(typeof(TerrainAwarePathfindingUtils),
                nameof(TerrainAwarePathfindingUtils.TryAddTeleporterNodes));

            codes.InsertRange(insertIndex, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldarg_3),
                new CodeInstruction(OpCodes.Ldarg, 5),
                loadCurIndex,
                loadUsedHeuristics,
                loadX2,
                new CodeInstruction(OpCodes.Call, method)
            });
        }

        if (octileIndex < 0)
        {
            Log.Error("[Better VPE Skipdoor pathing TMKCP] couldn't find OctileDistance position");
        }
        else
        {
            var heur = AccessTools.Method(typeof(TerrainAwarePathfindingUtils),
                nameof(TerrainAwarePathfindingUtils.Heuristics));
            codes[octileIndex] = new CodeInstruction(OpCodes.Call, heur);
            codes.InsertRange(octileIndex, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldarg_3),
                loadIndex4
            });
        }

        return codes;
    }
}