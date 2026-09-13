#nullable enable
using System;

namespace WorldGen.App.Storage
{
    /// <summary>
    /// Falióra az alkalmazásrétegnek (naplóidő, mentés dátuma). ND-109: a
    /// szimulációs kritikus útra nem kerülhet; ott a <c>SimulationTime</c> az idő.
    /// </summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
