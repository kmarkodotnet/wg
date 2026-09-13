#nullable enable

namespace WorldGen.App.Flow
{
    /// <summary>
    /// Cserélhető <see cref="IWorldSessionHost"/>: a flow-vezérlő induláskor ezt kapja,
    /// a Core-kötés később (vagy session-önként) állítja be a valódi célt.
    /// Cél nélkül: nincs nem mentett haladás, és nem menthető.
    /// </summary>
    public sealed class WorldSessionHostSlot : IWorldSessionHost
    {
        public IWorldSessionHost? Target { get; set; }

        public bool HasUnsavedChanges => Target?.HasUnsavedChanges ?? false;
        public bool CanSave => Target?.CanSave ?? false;
        public string? CurrentSavePath => Target?.CurrentSavePath;
        public string? CurrentWorldId => Target?.CurrentWorldId;
    }
}
