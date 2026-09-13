#nullable enable
using System;
using System.Globalization;

namespace WorldGen.App.Versioning
{
    public enum ReleaseChannel
    {
        Development,
        Alpha,
        Beta,
        ReleaseCandidate,
        Release,
    }

    /// <summary>
    /// A futó build azonossága (WF-REL-002). A Unity-kötés tölti fel
    /// (Application.version, build-script által írt build-szám); a naplóba,
    /// a főmenübe és a mentések fejlécébe ugyanez kerül.
    /// </summary>
    public sealed class BuildInfo
    {
        public string ProductName { get; }
        public SemanticVersion Version { get; }
        public int BuildNumber { get; }
        public ReleaseChannel Channel { get; }

        /// <summary>Nincs értéke, ha a build-script nem adta meg.</summary>
        public DateTime? BuildDateUtc { get; }

        public BuildInfo(string productName, SemanticVersion version, int buildNumber = 0, DateTime? buildDateUtc = null)
            : this(productName, version, buildNumber, InferChannel(version), buildDateUtc)
        {
        }

        public BuildInfo(string productName, SemanticVersion version, int buildNumber, ReleaseChannel channel, DateTime? buildDateUtc)
        {
            if (string.IsNullOrWhiteSpace(productName)) throw new ArgumentException("A terméknév nem lehet üres.", nameof(productName));
            if (buildNumber < 0) throw new ArgumentOutOfRangeException(nameof(buildNumber));
            ProductName = productName;
            Version = version ?? throw new ArgumentNullException(nameof(version));
            BuildNumber = buildNumber;
            Channel = channel;
            BuildDateUtc = buildDateUtc;
        }

        /// <summary>A főmenü sarkába: "v0.1.0-alpha" vagy "v0.1.0-alpha (build 12)".</summary>
        public string DisplayVersion
        {
            get
            {
                string s = "v" + Version.ToDisplayString();
                return BuildNumber > 0 ? s + " (build " + BuildNumber.ToString(CultureInfo.InvariantCulture) + ")" : s;
            }
        }

        public string ToLogLine()
        {
            string s = ProductName + " " + Version + " build " + BuildNumber.ToString(CultureInfo.InvariantCulture)
                + " channel " + Channel;
            if (BuildDateUtc.HasValue)
                s += " built " + BuildDateUtc.Value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            return s;
        }

        /// <summary>
        /// Csatorna a pre-release első azonosítójából: dev → Development,
        /// alpha → Alpha, beta → Beta, rc → ReleaseCandidate, nincs → Release.
        /// Ismeretlen azonosító Development (óvatos alapérték).
        /// </summary>
        public static ReleaseChannel InferChannel(SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            if (!version.IsPreRelease) return ReleaseChannel.Release;
            string first = version.PreRelease.Split('.')[0].ToLowerInvariant();
            switch (first)
            {
                case "alpha": return ReleaseChannel.Alpha;
                case "beta": return ReleaseChannel.Beta;
                case "rc": return ReleaseChannel.ReleaseCandidate;
                default: return ReleaseChannel.Development;
            }
        }

        /// <summary>
        /// A repo-gyökér VERSION fájljából az első nem üres sort értelmezi
        /// (a fájl további sorai emberi megjegyzések).
        /// </summary>
        public static bool TryParseVersionFile(string? content, out SemanticVersion? version)
        {
            version = null;
            if (content == null) return false;
            foreach (string raw in content.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                return SemanticVersion.TryParse(line, out version);
            }
            return false;
        }
    }
}
