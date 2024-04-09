using JetBrains.Annotations;
using Verse;

namespace BetterVPESkipdoorPathing;

[UsedImplicitly]
public class Settings: ModSettings
{
    public static Allowed Allowed = Allowed.Everyone;
    
    public override void ExposeData()
    {
        Scribe_Values.Look(ref Allowed, "BetterVPESkipdoorPathing.OnlyColonists", Allowed.Everyone);

        base.ExposeData();
    }
}

public enum Allowed : int
{
    Everyone=0,
    Friendlies=1,
    Colonists=2,
}