#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using WorldGen.App.Serialization;

namespace WorldGen.App.Versioning
{
    /// <summary>
    /// A build által írt <c>StreamingAssets/build-info.json</c> (ND-111): így a futó
    /// alkalmazás pontosan tudja a verzióját, build-számát és a build idejét.
    /// </summary>
    public static class BuildInfoCodec
    {
        private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        public static string ToJson(BuildInfo info)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            var obj = JsonValue.CreateObject()
                .Set("productName", info.ProductName)
                .Set("version", info.Version.ToString())
                .Set("buildNumber", info.BuildNumber)
                .Set("channel", info.Channel.ToString())
                .Set("buildDateUtc", info.BuildDateUtc.HasValue
                    ? JsonValue.FromString(info.BuildDateUtc.Value.ToString(DateFormat, CultureInfo.InvariantCulture))
                    : JsonValue.Null);
            return JsonWriter.Write(obj) + "\n";
        }

        public static bool TryParse(string? text, out BuildInfo? info)
        {
            info = null;
            if (text == null || !JsonParser.TryParse(text, out var root, out _) || root == null || root.Kind != JsonValueKind.Object) return false;
            if (!(root.GetMember("productName") is JsonValue p) || !p.TryGetString(out string? product) || string.IsNullOrWhiteSpace(product)) return false;
            if (!(root.GetMember("version") is JsonValue v) || !v.TryGetString(out string? versionText) || !SemanticVersion.TryParse(versionText, out var version)) return false;

            int buildNumber = 0;
            if (root.GetMember("buildNumber") is JsonValue b && (!b.TryGetInt32(out buildNumber) || buildNumber < 0)) return false;

            var channel = BuildInfo.InferChannel(version);
            if (root.GetMember("channel") is JsonValue c && c.TryGetString(out string? channelText) && channelText.Length > 0 && char.IsLetter(channelText[0])
                && Enum.TryParse(channelText, false, out ReleaseChannel parsed) && Enum.IsDefined(typeof(ReleaseChannel), parsed))
                channel = parsed;

            DateTime? date = null;
            if (root.GetMember("buildDateUtc") is JsonValue d && d.TryGetString(out string? dateText))
            {
                if (!DateTime.TryParseExact(dateText, DateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate))
                    return false;
                date = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
            }

            info = new BuildInfo(product, version, buildNumber, channel, date);
            return true;
        }
    }

    /// <summary>
    /// A kiadási identitás egyetlen forrása (<c>tools/release/release-identity.json</c>, ND-111):
    /// terméknév, cégnév, futtatható név, alkalmazásverzió, tartalék scene-lista.
    /// A build-script, a csomagoló és a telepítő ugyanezt olvassa.
    /// </summary>
    public sealed class ReleaseIdentity
    {
        public const string FormatId = "worldgen.release-identity";
        public const int FormatVersion = 1;

        private ReleaseIdentity(string productName, string companyName, string executableName, SemanticVersion version, IReadOnlyList<string> fallbackScenes)
        {
            ProductName = productName;
            CompanyName = companyName;
            ExecutableName = executableName;
            Version = version;
            FallbackScenes = fallbackScenes;
        }

        public string ProductName { get; }

        /// <summary>A Unity <c>persistentDataPath</c> része: az első kiadás után nem változhat (ND-111).</summary>
        public string CompanyName { get; }

        public string ExecutableName { get; }
        public SemanticVersion Version { get; }

        /// <summary>Akkor használt, ha az EditorBuildSettings scene-listája üres.</summary>
        public IReadOnlyList<string> FallbackScenes { get; }

        public string BuildFolderName => ExecutableName + "-" + Version.ToDisplayString() + "-win64";
        public string PortableZipFileName => BuildFolderName + ".zip";
        public string InstallerBaseName => BuildFolderName + "-setup";

        /// <summary>Windows VERSIONINFO: négy, egyenként 0–65535 közötti szám.</summary>
        public string NumericFileVersion(int buildNumber)
        {
            if (buildNumber < 0 || buildNumber > 65535) throw new ArgumentOutOfRangeException(nameof(buildNumber));
            if (Version.Major > 65535 || Version.Minor > 65535 || Version.Patch > 65535)
                throw new InvalidOperationException("A verziószám nem fér a Windows VERSIONINFO-ba: " + Version);
            return Version.Major.ToString(CultureInfo.InvariantCulture) + "." + Version.Minor.ToString(CultureInfo.InvariantCulture) + "."
                + Version.Patch.ToString(CultureInfo.InvariantCulture) + "." + buildNumber.ToString(CultureInfo.InvariantCulture);
        }

        public static ReleaseIdentity Parse(string text)
        {
            JsonValue root;
            try
            {
                root = JsonParser.Parse(text ?? "");
            }
            catch (JsonFormatException ex)
            {
                throw new FormatException("release-identity: érvénytelen JSON: " + ex.Message, ex);
            }
            if (root.Kind != JsonValueKind.Object) throw Fail("a gyökér nem objektum");
            if (RequiredString(root, "format") != FormatId) throw Fail("ismeretlen formátum");
            if (!(root.GetMember("formatVersion") is JsonValue fv) || !fv.TryGetInt32(out int formatVersion) || formatVersion != FormatVersion)
                throw Fail("nem támogatott formatVersion");

            string product = RequiredString(root, "productName").Trim();
            string company = RequiredString(root, "companyName").Trim();
            string exe = RequiredString(root, "executableName").Trim();
            if (product.Length == 0 || company.Length == 0) throw Fail("üres termék- vagy cégnév");
            if (!IsSafeName(exe)) throw Fail("az executableName csak [A-Za-z0-9._-] lehet: " + exe);
            if (!IsSafePathSegment(product) || !IsSafePathSegment(company)) throw Fail("a termék- és cégnév mappanévként is érvényes kell legyen");
            if (!SemanticVersion.TryParse(RequiredString(root, "version"), out var version)) throw Fail("érvénytelen version");

            var scenes = new List<string>();
            var sceneNode = root.GetMember("fallbackScenes");
            if (sceneNode != null)
            {
                if (sceneNode.Kind != JsonValueKind.Array) throw Fail("a fallbackScenes tömb kell legyen");
                foreach (var s in sceneNode.Items)
                {
                    if (!s.TryGetString(out string? scene) || !scene.StartsWith("Assets/", StringComparison.Ordinal) || !scene.EndsWith(".unity", StringComparison.Ordinal))
                        throw Fail("érvénytelen scene-út: " + s);
                    scenes.Add(scene);
                }
            }
            return new ReleaseIdentity(product, company, exe, version, scenes);
        }

        private static string RequiredString(JsonValue obj, string name)
            => obj.GetMember(name) is JsonValue v && v.TryGetString(out string? s) ? s : throw Fail("hiányzó mező: " + name);

        private static bool IsSafeName(string s)
        {
            if (s.Length == 0 || s.Length > 64) return false;
            foreach (char ch in s)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '_' || ch == '-';
                if (!ok) return false;
            }
            return true;
        }

        private static bool IsSafePathSegment(string s)
            => s.Length <= 64 && s == WorldGen.App.Storage.FileNameSanitizer.Sanitize(s, "x", 64);

        private static FormatException Fail(string message) => new FormatException("release-identity: " + message);
    }
}
