using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using VFECore;

namespace BetterVPESkipdoorPathing;

[UsedImplicitly]
[HarmonyPatch(typeof(PathFinder), nameof(PathFinder.FindPath), typeof(IntVec3), typeof(LocalTargetInfo),
    typeof(TraverseParms), typeof(PathEndMode), typeof(PathFinderCostTuning))]
static class Verse_PathFinder_FindPath_Patch
{
    [UsedImplicitly]
    static void Postfix()
    {
        //clean params, so new will be created on next call
        pathfindingParams = null;
        teleporters = null;
        octileDistanceFromDestToTeleport = -1;
    }

    [UsedImplicitly]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        CodeInstruction loadCurIndex = null;
        CodeInstruction loadUsedHeuristics = null;
        CodeInstruction loadX2 = null;
        CodeInstruction loadIndex4 = null;

        var fld_parentIndex = AccessTools.Field(typeof(PathFinder.PathFinderNodeFast),
            nameof(PathFinder.PathFinderNodeFast.parentIndex));
        var mtd_FinalizedPath = AccessTools.Method(typeof(PathFinder), nameof(PathFinder.FinalizedPath));
        var mtd_OctileDistance = AccessTools.Method(typeof(GenMath), nameof(GenMath.OctileDistance));
        var fld_costNodeCost = AccessTools.Field(typeof(PathFinder.PathFinderNodeFast),
            nameof(PathFinder.PathFinderNodeFast.costNodeCost));
        var fld_status = AccessTools.Field(typeof(PathFinder.PathFinderNodeFast),
            nameof(PathFinder.PathFinderNodeFast.status));
        var fld_calcGrid = AccessTools.Field(typeof(PathFinder), nameof(PathFinder.calcGrid));
        var fld_statusClosedValue = AccessTools.Field(typeof(PathFinder), nameof(PathFinder.statusClosedValue));

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
                loadCurIndex = new CodeInstruction(prev);
            }

            //usedRegionHeuristics - ldloc.s      usedRegionHeuristics
            //                       call         instance class Verse.AI.PawnPath Verse.AI.PathFinder::FinalizedPath(int32, bool)
            if (loadUsedHeuristics == null && code.Calls(mtd_FinalizedPath) && prev.IsLdloc())
            {
                loadUsedHeuristics = new CodeInstruction(prev);
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
            Log.Error("[Better VPE Skipdoor pathing] couldn't find insert position");
        }
        else
        {
            var method = AccessTools.Method(typeof(Verse_PathFinder_FindPath_Patch), nameof(TryAddTeleporterNodes));

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
            Log.Error("[Better VPE Skipdoor pathing] couldn't find OctileDistance position");
        }
        else
        {
            var heur = AccessTools.Method(typeof(Verse_PathFinder_FindPath_Patch), nameof(Heuristics));
            codes[octileIndex] = new CodeInstruction(OpCodes.Call, heur);
            codes.InsertRange(octileIndex, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_2),
                loadIndex4
            });
        }

        return codes;
    }

    public static void TryAddTeleporterNodes(PathFinder pathfinder, IntVec3 start, LocalTargetInfo dest,
        TraverseParms traverseParms, PathFinderCostTuning tuning, int curIndex, bool usedRegionHeuristics,
        ref int openedNodes)
    {
        var p = GetParams(pathfinder, start, dest, traverseParms);

        AddTeleporterNodes(pathfinder, start, dest, traverseParms, tuning, curIndex, usedRegionHeuristics,
            ref openedNodes, p.teleports, p.pawn, p.heuristicStrength, p.ticksPerMoveCardinal, p.ticksPerMoveDiagonal,
            p.avoidGrid, p.allowedArea);
    }

    private class PathfindingParams
    {
        public List<int> teleports;
        public Pawn pawn;
        public float heuristicStrength;
        public int ticksPerMoveCardinal;
        public int ticksPerMoveDiagonal;
        public ByteGrid avoidGrid;
        public Area allowedArea;
    }

    private static List<DoorTeleporter> teleporters;
    private static int octileDistanceFromDestToTeleport = -1;
    private static PathfindingParams pathfindingParams;

    private static void InitTeleporters(Map map, IntVec3 dest, int cardinal, int diagonal)
    {
        if (teleporters != null)
        {
            return;
        }
        
        teleporters = WorldComponent_DoorTeleporterManager.Instance.DoorTeleporters
            .Where(x => x is {Spawned: true} && x.Map == map)
            .ToList();
        
        octileDistanceFromDestToTeleport = GetOctileDistanceToClosestTeleport(dest, cardinal, diagonal);
    }

    private static PathfindingParams GetParams(PathFinder pathfinder, IntVec3 start, LocalTargetInfo dest,
        TraverseParms traverseParms)
    {
        if (pathfindingParams != null)
        {
            return pathfindingParams;
        }

        pathfindingParams = new PathfindingParams();
        if (pathfindingParams.pawn != null)
        {
            pathfindingParams.ticksPerMoveCardinal = pathfindingParams.pawn.TicksPerMoveCardinal;
            pathfindingParams.ticksPerMoveDiagonal = pathfindingParams.pawn.TicksPerMoveDiagonal;
        }
        else
        {
            pathfindingParams.ticksPerMoveCardinal = PathFinder.DefaultMoveTicksCardinal;
            pathfindingParams.ticksPerMoveDiagonal = PathFinder.DefaultMoveTicksDiagonal;
        }
        
        InitTeleporters(pathfinder.map, dest.Cell, pathfindingParams.ticksPerMoveCardinal, pathfindingParams.ticksPerMoveDiagonal);

        var indices = pathfinder.map.cellIndices;

        pathfindingParams.teleports = teleporters
            .Select(x => indices.CellToIndex(x.Position))
            .ToList();

        pathfindingParams.pawn = traverseParms.pawn;
        pathfindingParams.allowedArea = pathfinder.GetAllowedArea(pathfindingParams.pawn);
        pathfindingParams.avoidGrid = traverseParms.alwaysUseAvoidGrid
            ? pathfinder.map.avoidGrid.Grid
            : pathfindingParams.pawn?.GetAvoidGrid();
        pathfindingParams.heuristicStrength =
            pathfinder.DetermineHeuristicStrength(pathfindingParams.pawn, start, dest);

        return pathfindingParams;
    }

    private static int Heuristics(int dx, int dz, int cardinal, int diagonal, PathFinder pathfinder, LocalTargetInfo dest, int cellIndex)
    {
        InitTeleporters(pathfinder.map, dest.Cell, cardinal, diagonal);
        var octileDistance = GenMath.OctileDistance(dx, dz, cardinal, diagonal);
        var gateToCell = GetOctileDistanceToClosestTeleport(pathfinder.map.cellIndices.IndexToCell(cellIndex), cardinal, diagonal);
        return Math.Min(octileDistance, gateToCell + octileDistanceFromDestToTeleport);
    }

    private static int GetOctileDistanceToClosestTeleport(IntVec3 cell, int cardinal, int diagonal)
    {
        if (teleporters.NullOrEmpty())
        {
            return int.MaxValue;
        }

        return teleporters
            .Select(x => x.Position)
            .Select(p => GenMath.OctileDistance(Math.Abs(p.x - cell.x), Math.Abs(p.z - cell.z), cardinal, diagonal))
            .Min();
    }

    private static void AddTeleporterNodes(PathFinder pathfinder, IntVec3 start, LocalTargetInfo dest,
        TraverseParms traverseParms, PathFinderCostTuning tuning, int curIndex, bool usedRegionHeuristics,
        ref int openedNodes, List<int> teleports, Pawn pawn,
        float heuristicStrength, int ticksPerMoveCardinal, int ticksPerMoveDiagonal, ByteGrid avoidGrid,
        Area allowedArea)
    {
        if (!teleports.Contains(curIndex))
        {
            return;
        }

        foreach (var index in teleports)
        {
            if (index == curIndex)
            {
                continue;
            }

            if (PathFinder.calcGrid[index].status == PathFinder.statusClosedValue
                && !usedRegionHeuristics)
            {
                continue;
            }

            var pathCost = ticksPerMoveCardinal;

            if (avoidGrid != null)
                pathCost += avoidGrid[index] * 8;
            if (allowedArea != null && !allowedArea[index])
                pathCost += PathFinder.Cost_OutsideAllowedArea;


            var b = pathfinder.edificeGrid[index];
            if (b != null)
            {
                var buildingCost =
                    PathFinder.GetBuildingCost(b, traverseParms, pawn, tuning);
                if (buildingCost == int.MaxValue)
                {
                    continue;
                }

                pathCost += buildingCost;
            }

            var blueprintList = pathfinder.blueprintGrid[index];
            if (blueprintList != null)
            {
                var a = 0;
                for (var i = 0; i < blueprintList.Count; ++i)
                {
                    a = Mathf.Max(a, PathFinder.GetBlueprintCost(blueprintList[i], pawn));
                }

                if (a == int.MaxValue)
                {
                    continue;
                }

                pathCost += a;
            }

            var num15 = pathCost + PathFinder.calcGrid[curIndex].knownCost;
            var status = PathFinder.calcGrid[index].status;
            if (status == PathFinder.statusClosedValue ||
                status == PathFinder.statusOpenValue)
            {
                var num16 = 0;
                if (status == PathFinder.statusClosedValue)
                    num16 = ticksPerMoveCardinal;
                if (PathFinder.calcGrid[index].knownCost <= num15 + num16)
                    continue;
            }

            if (usedRegionHeuristics)
            {
                PathFinder.calcGrid[index].heuristicCost =
                    Mathf.RoundToInt(
                        pathfinder.regionCostCalculator.GetPathCostFromDestToRegion(
                            index) *
                        PathFinder.RegionHeuristicWeightByNodesOpened.Evaluate(openedNodes));
                if (PathFinder.calcGrid[index].heuristicCost < 0)
                {
                    Log.ErrorOnce(
                        $"Heuristic cost overflow for {pawn.ToStringSafe()} pathing from {start} to {dest}.",
                        pawn.GetHashCode() ^ 193840009);
                    PathFinder.calcGrid[index].heuristicCost = 0;
                }
            }
            else if (status != PathFinder.statusClosedValue &&
                     status != PathFinder.statusOpenValue)
            {
                var newCell = pathfinder.map.cellIndices.IndexToCell(index);
                var num17 = GenMath.OctileDistance(Math.Abs(dest.Cell.x - newCell.x), Math.Abs(dest.Cell.z - newCell.z),
                    ticksPerMoveCardinal, ticksPerMoveDiagonal);
                PathFinder.calcGrid[index].heuristicCost = Mathf.RoundToInt(num17 * heuristicStrength);
            }

            var priority2 = num15 + PathFinder.calcGrid[index].heuristicCost;
            if (priority2 < 0)
            {
                Log.ErrorOnce(
                    $"Node cost overflow for {pawn.ToStringSafe()} pathing from {start} to {dest}.",
                    pawn.GetHashCode() ^ 87865822);
                priority2 = 0;
            }

            PathFinder.calcGrid[index].parentIndex = curIndex;
            PathFinder.calcGrid[index].knownCost = num15;
            PathFinder.calcGrid[index].status = PathFinder.statusOpenValue;
            PathFinder.calcGrid[index].costNodeCost = priority2;
            ++openedNodes;
            pathfinder.openList.Enqueue(index, priority2);
        }
    }
}