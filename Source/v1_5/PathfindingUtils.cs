﻿using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using VanillaPsycastsExpanded.Skipmaster;
using Verse;
using Verse.AI;
using VFECore;

namespace BetterVPESkipdoorPathing;

public static class PathfindingUtils
{
    private static readonly JobDef[] NoTeleportJobs =
    {
        JobDefOf.GotoWander,
        JobDefOf.Wait_Wander,
    };

    private static readonly JobDef[] FollowJobs =
    {
        JobDefOf.FollowRoper,
        JobDefOf.Follow,
        JobDefOf.FollowClose,
    };
    
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
    
    private static List<Skipdoor> teleporters;
    private static int octileDistanceFromDestToTeleport = -1;
    private static PathfindingParams pathfindingParams;
    
     private static void InitTeleporters(Map map, IntVec3 dest, int cardinal, int diagonal)
    {
        if (teleporters != null)
        {
            return;
        }

        teleporters = WorldComponent_DoorTeleporterManager.Instance.DoorTeleporters
            .OfType<Skipdoor>()
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

        pathfindingParams = new PathfindingParams
        {
            pawn = traverseParms.pawn
        };
        
        if (pathfindingParams.pawn != null)
        {
            pathfindingParams.ticksPerMoveCardinal = Mathf.RoundToInt(pathfindingParams.pawn.TicksPerMoveCardinal);
            pathfindingParams.ticksPerMoveDiagonal = Mathf.RoundToInt(pathfindingParams.pawn.TicksPerMoveDiagonal);
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

        pathfindingParams.allowedArea = pathfinder.GetAllowedArea(pathfindingParams.pawn);
        pathfindingParams.avoidGrid = traverseParms.alwaysUseAvoidGrid
            ? pathfinder.map.avoidGrid.Grid
            : pathfindingParams.pawn?.GetAvoidGrid();
        pathfindingParams.heuristicStrength =
            pathfinder.DetermineHeuristicStrength(pathfindingParams.pawn, start, dest);

        return pathfindingParams;
    }
    
    private static bool CanUseTeleporters(Pawn pawn, LocalTargetInfo dest)
    {
        if (pawn == null)
        {
            return false;
        }

        if (pawn.InMentalState)
        {
            return false;
        }

        var curJobDef = pawn.CurJob?.def;
        if (NoTeleportJobs.Contains(curJobDef))
        {
            return false;
        }

        var faction = pawn.Faction;
        var allowed = Settings.Allowed;
        
        switch (allowed)
        {
            case Allowed.Friendlies when faction.HostileTo(Faction.OfPlayer):
            case Allowed.Colonists when !faction.IsPlayerSafe():
                return false;
            default:
                return pawn.def.race.intelligence switch
                {
                    Intelligence.Humanlike => true,
                    Intelligence.ToolUser => true,
                    Intelligence.Animal => FollowJobs.Contains(curJobDef) && dest.HasThing,
                    _ => true
                };
        }
    }

    public static void TryAddTeleporterNodes(PathFinder pathfinder, IntVec3 start, LocalTargetInfo dest,
        TraverseParms traverseParms, PathFinderCostTuning tuning, int curIndex, bool usedRegionHeuristics,
        ref int openedNodes)
    {
        if (!CanUseTeleporters(traverseParms.pawn, dest))
        {
            return;
        }

        var p = GetParams(pathfinder, start, dest, traverseParms);

        AddTeleporterNodes(pathfinder, start, dest, traverseParms, tuning, curIndex, usedRegionHeuristics,
            ref openedNodes, p.teleports, p.pawn, p.heuristicStrength, p.ticksPerMoveCardinal, p.ticksPerMoveDiagonal,
            p.avoidGrid, p.allowedArea);
    }

    public static int Heuristics(int dx, int dz, int cardinal, int diagonal, PathFinder pathfinder,
        LocalTargetInfo dest, TraverseParms traverseParms, int cellIndex)
    {
        var octileDistance = GenMath.OctileDistance(dx, dz, cardinal, diagonal);
        if (!CanUseTeleporters(traverseParms.pawn, dest))
        {
            return octileDistance;
        }

        InitTeleporters(pathfinder.map, dest.Cell, cardinal, diagonal);
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

            var pathCost = ticksPerMoveCardinal * Settings.PathingCost;

            if (avoidGrid != null)
            {
                pathCost += avoidGrid[index] * 8;
            }

            if (allowedArea != null && !allowedArea[index])
            {
                pathCost += PathFinder.Cost_OutsideAllowedArea;
            }


            var b = pathfinder.edificeGrid[index];
            if (b != null)
            {
                var buildingCost = PathFinder.GetBuildingCost(b, traverseParms, pawn, tuning);
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
            if (status == PathFinder.statusClosedValue || status == PathFinder.statusOpenValue)
            {
                var num16 = 0;
                if (status == PathFinder.statusClosedValue)
                {
                    num16 = ticksPerMoveCardinal;
                }

                if (PathFinder.calcGrid[index].knownCost <= num15 + num16)
                {
                    continue;
                }
            }

            if (usedRegionHeuristics)
            {
                PathFinder.calcGrid[index].heuristicCost = Mathf.RoundToInt(
                    pathfinder.regionCostCalculator.GetPathCostFromDestToRegion(index) *
                    PathFinder.RegionHeuristicWeightByNodesOpened.Evaluate(openedNodes));
                if (PathFinder.calcGrid[index].heuristicCost < 0)
                {
                    Log.ErrorOnce(
                        $"Heuristic cost overflow for {pawn.ToStringSafe()} pathing from {start} to {dest}.",
                        pawn.GetHashCode() ^ 193840009);
                    PathFinder.calcGrid[index].heuristicCost = 0;
                }
            }
            else if (status != PathFinder.statusClosedValue && status != PathFinder.statusOpenValue)
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

    public static void ResetParams()
    {
        //clean params, so new will be created on next call
        pathfindingParams = null;
        teleporters = null;
        octileDistanceFromDestToTeleport = -1;
    }
}