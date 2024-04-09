using JetBrains.Annotations;
using Verse;

namespace BetterVPESkipdoorPathing;

[UsedImplicitly]
public class Settings: ModSettings
{
    public static Allowed Allowed = Allowed.Everyone;
    public static int PathingCost = 1;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref Allowed, "BetterVPESkipdoorPathing.OnlyColonists", Allowed.Everyone);
        Scribe_Values.Look(ref PathingCost, "BetterVPESkipdoorPathing.PathingCost", 1);

        base.ExposeData();
    }
}

public enum Allowed : int
{
    Everyone=0,
    Friendlies=1,
    Colonists=2,
}