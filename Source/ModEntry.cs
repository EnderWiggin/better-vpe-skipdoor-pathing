using HarmonyLib;
using Verse;

namespace BetterVPESkipdoorPathing;

// ReSharper disable once ClassNeverInstantiated.Global
public class ModEntry : Mod
{
    public ModEntry(ModContentPack content) : base(content)
    {
        new Harmony(Content.PackageIdPlayerFacing).PatchAll();
    }
}