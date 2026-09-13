#nullable enable
using System.Collections.Generic;
using System.Globalization;

namespace WorldGen.App.Diagnostics
{
    /// <summary>
    /// A napló elejére kerülő hardver- és környezetadatok (WF-DIAG-001).
    /// A Unity-kötés tölti fel a <c>SystemInfo</c> / <c>Application</c> értékeiből;
    /// ami nem kérdezhető le, üres vagy null marad, és nem kerül a naplóba.
    /// </summary>
    public sealed class SystemInfoReport
    {
        public string ApplicationVersion { get; set; } = "";
        public string EngineVersion { get; set; } = "";
        public string OperatingSystem { get; set; } = "";
        public string CpuModel { get; set; } = "";
        public int CpuLogicalCores { get; set; }
        public long? SystemMemoryMb { get; set; }
        public string GpuModel { get; set; } = "";
        public string GraphicsApi { get; set; } = "";
        public long? GraphicsMemoryMb { get; set; }
        public string DisplayResolution { get; set; } = "";

        public IReadOnlyList<string> ToLogLines()
        {
            var lines = new List<string>();
            Add(lines, "Application", ApplicationVersion);
            Add(lines, "Engine", EngineVersion);
            Add(lines, "OS", OperatingSystem);
            if (CpuModel.Length > 0)
            {
                string cpu = CpuModel;
                if (CpuLogicalCores > 0) cpu += " (" + CpuLogicalCores.ToString(CultureInfo.InvariantCulture) + " logical cores)";
                lines.Add("CPU: " + cpu);
            }
            if (SystemMemoryMb.HasValue) lines.Add("RAM: " + SystemMemoryMb.Value.ToString(CultureInfo.InvariantCulture) + " MB");
            Add(lines, "GPU", GpuModel);
            Add(lines, "Graphics API", GraphicsApi);
            if (GraphicsMemoryMb.HasValue) lines.Add("VRAM: " + GraphicsMemoryMb.Value.ToString(CultureInfo.InvariantCulture) + " MB");
            Add(lines, "Display", DisplayResolution);
            return lines;
        }

        private static void Add(List<string> lines, string label, string value)
        {
            if (!string.IsNullOrEmpty(value)) lines.Add(label + ": " + value);
        }
    }
}
