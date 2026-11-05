namespace HollowMario
{
    internal enum CapKind { None, Wing, Vanish, Metal }

    internal sealed class CapDefinition
    {
        internal readonly CapKind Kind;
        internal readonly string Name, Unlock;
        internal readonly uint NativeFlag;
        internal readonly bool Implemented;
        internal readonly float DamageMultiplier;
        internal readonly int HitSoulCost;
        internal readonly float UpkeepMultiplier;
        internal string Lesson => Kind==CapKind.Wing?"Cyclone Slash":Kind==CapKind.Vanish?"Great Slash":"Dash Slash";
        internal string EquipHint => Kind==CapKind.Wing?"Triple jump to take off. Up climbs, down dives; turn outside flight.":Kind==CapKind.Metal?"Hazards bounce you. Hits cost soul, not masks. Sink in water and acid.":"";
        private CapDefinition(CapKind kind, string name, string unlock, uint flag, bool implemented, float damage = 1f, int hitCost = 0, float upkeepMultiplier = 1f)
        { Kind=kind; Name=name; Unlock=unlock; NativeFlag=flag; Implemented=implemented; DamageMultiplier=damage; HitSoulCost=hitCost; UpkeepMultiplier=upkeepMultiplier; }

        internal static readonly CapDefinition Wing = new CapDefinition(CapKind.Wing,"Wing Cap","hasCyclone",8,true,upkeepMultiplier:.75f);
        internal static readonly CapDefinition Vanish = new CapDefinition(CapKind.Vanish,"Vanish Cap","hasUpwardSlash",2,true);
        internal static readonly CapDefinition Metal = new CapDefinition(CapKind.Metal,"Metal Cap","hasDashSlash",4,true,1.5f,5);
        internal static CapDefinition For(CapKind kind) => kind==CapKind.Wing?Wing:kind==CapKind.Vanish?Vanish:kind==CapKind.Metal?Metal:null;
    }

    internal interface ICapHost
    {
        bool Available { get; }
        bool Glory { get; }
        int Soul { get; }
        bool Unlocked(CapDefinition cap);
        bool Apply(CapDefinition cap);
        void Clear();
        int Spend(int amount);
    }
}
