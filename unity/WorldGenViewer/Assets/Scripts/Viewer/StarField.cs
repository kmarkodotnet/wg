using UnityEngine;
using WorldGen.Core.Random;

namespace WorldGen.Viewer
{
    /// <summary>
    /// FELHASZNÁLÓI KÉRÉS (2026-09-06): "a hátter legyen csillagos ég...
    /// procedurális, de olyan hogy a bolygó forgatásával forogjon
    /// szinkronban, de a bolygó alap tengelyforgása miatt látszólag is
    /// mozogjanak a csillagok". Determinisztikus (a worldSeedből generált,
    /// ld. RandomDomain.Decorative doksi - docs/04-decisions.md ND-51),
    /// pontszerű (kis, kifelé néző kvad-"billboard") csillag-mező.
    ///
    /// GEOMETRIAI KONVENCIÓ: minden csillag egy kis, a gömb-KÖZÉPPONTBÓL
    /// KIFELÉ néző kvad (nem kamera felé forduló klasszikus billboard) -
    /// mivel a csillagszféra sugara (alapból 5000) sok EZERSZERESE a
    /// kamera lehetséges elmozdulásának (PlanetOrbitCamera max ~800), a
    /// kétféle tájolás vizuálisan megkülönböztethetetlen, de ez az
    /// egyszerűbb/olcsóbb: a geometria EGYSZER épül fel, nincs
    /// kamera-követő újraszámítás.
    ///
    /// FORGATÁSI KONVENCIÓ: ezt a projektet a Nap-irány forgatása (ld.
    /// SunController), NEM a bolygó-mesh forgatása vezérli - a terep
    /// test-keretben (Unity-ben Y = pólustengely, ld.
    /// BodyFrameConversion) FIX marad. A csillagok - amik a (közelítőleg)
    /// INERCIA-keretben fixek - EBBEN a konvencióban a bolygó SAJÁT
    /// tengelyforgásával ELLENTÉTES irányban kell elforduljanak ahhoz,
    /// hogy a (fixen álló) terkephez képest helyesen "seperjenek végig az
    /// égen" - ld. <see cref="SetRotationAngleRadians"/>, amit a
    /// SunController hív minden képkockán UGYANAZZAL a forgási szöggel,
    /// amit a Nap napi (tengelyforgás-eredetű) mozgásához is használ.
    /// </summary>
    public class StarField : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("A csillagkép forrása - a determinisztikus katalógus ebből a " +
                 "worldSeedből generálódik (ugyanaz a seed -> ugyanaz a csillagkép).")]
        private PlanetGridMesh planetGridMesh;

        [SerializeField] private int starCount = 2500;
        [SerializeField] private float sphereRadius = 5000f;
        [SerializeField] private float starSize = 12f;
        [SerializeField] private Material starMaterial;

        private ulong _builtForSeed;
        private bool _hasBuilt;

        private void Start()
        {
            EnsureBuilt();
        }

        /// <summary>
        /// Csak akkor épít újra, ha még soha nem épült, VAGY a worldSeed
        /// megváltozott azóta - a SunController minden képkockán hívhatja
        /// ezt (a rotáció-frissítés előtt) anélkül, hogy ez drága lenne.
        /// </summary>
        public void EnsureBuilt()
        {
            if (planetGridMesh == null)
                return;
            // HIBAJAVITAS (code-review-ban feltart hianyossag, 2026-09-06): a
            // SunController `[ExecuteAlways]`, es az `OnValidate()`-je (ami
            // Edit modban, PLAY NELKUL is lefut - pl. amikor az Inspectorban
            // a `starField` mezot beallitjuk) meghivja ezt a metodust. Unity
            // NEM engedi `AddComponent`/`new Material`-t hivni `OnValidate`
            // belsejebol (hibat logolna) - ezert az ELSO (mesh-epito) hivas
            // csak Play kozben tortenhet meg. A "Rebuild Stars" context-menu
            // (explicit felhasznaloi akcio, NEM automatikus callback) ETTOL
            // FUGGETLENUL, kozvetlenul a `Build()`-et hivja, tehat Edit
            // modban is mukodik, ha valaki kezzel keri.
            if (!Application.isPlaying)
                return;
            ulong seed = planetGridMesh.WorldSeedUnsigned;
            if (_hasBuilt && seed == _builtForSeed)
                return;
            Build(seed);
        }

        /// <summary>
        /// A bolygó tengelyforgásával ELLENTÉTES forgás a pólustengely
        /// (Unity Y, ld. BodyFrameConversion) körül - ld. osztály-doksi.
        /// </summary>
        public void SetRotationAngleRadians(double angleRadians)
        {
            transform.localRotation = Quaternion.AngleAxis(-(float)(angleRadians * Mathf.Rad2Deg), Vector3.up);
        }

        /// <summary>
        /// A csillagmezőt a világtérben rögzíti. A StarField a scene-ben a
        /// Planet gyereke, ezért a localRotation identitásra állítása nem
        /// elég: azzal továbbra is örökölné a bolygó tengelyforgását.
        /// A world rotation képkockánkénti identitásra állítása pontosan
        /// kompenzálja a szülő aktuális forgását.
        /// </summary>
        public void KeepFixedInWorldSpace()
        {
            transform.rotation = Quaternion.identity;
        }

        [ContextMenu("Rebuild Stars")]
        private void RebuildFromContextMenu()
        {
            if (planetGridMesh == null)
                return;
            Build(planetGridMesh.WorldSeedUnsigned);
        }

        private void Build(ulong worldSeed)
        {
            var vertices = new Vector3[starCount * 4];
            var colors = new Color[starCount * 4];
            var uvs = new Vector2[starCount * 4];
            var triangles = new int[starCount * 6];

            for (int i = 0; i < starCount; i++)
            {
                DeterministicRandom.SampleUnitVector3(
                    worldSeed, RandomDomain.Decorative, (ulong)i, 0,
                    out double dx, out double dy, out double dz,
                    RandomProperty.StarPosition);
                Vector3 dir = BodyFrameConversion.ToUnity(dx, dy, dz).normalized;
                Vector3 center = dir * sphereRadius;

                // Tetszőleges, dir-re merőleges (tangent1,tangent2) bázis -
                // a "fel" segédvektor a dir-hez legkevésbé közeli tengely,
                // hogy sose fajuljon el (nullvektor-közeli kereszt-szorzat).
                Vector3 helper = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) < 0.99f ? Vector3.up : Vector3.right;
                Vector3 tangent1 = Vector3.Cross(helper, dir).normalized;
                Vector3 tangent2 = Vector3.Cross(dir, tangent1);

                double brightness = DeterministicRandom.Sample(
                    worldSeed, RandomDomain.Decorative, (ulong)i, 0,
                    RandomProperty.StarBrightness);
                // [0.35, 1.0] - egyetlen csillag se legyen teljesen
                // láthatatlan, de legyen fényesség-változatosság.
                float b = (float)(0.35 + 0.65 * brightness);
                Color color = new Color(b, b, b, 1f);

                float half = starSize * 0.5f;
                int vBase = i * 4;
                vertices[vBase + 0] = center - tangent1 * half - tangent2 * half;
                vertices[vBase + 1] = center + tangent1 * half - tangent2 * half;
                vertices[vBase + 2] = center + tangent1 * half + tangent2 * half;
                vertices[vBase + 3] = center - tangent1 * half + tangent2 * half;
                colors[vBase + 0] = color; colors[vBase + 1] = color;
                colors[vBase + 2] = color; colors[vBase + 3] = color;
                // UV a shader kör alakú (kör-lágyítású) elhalványításához
                // (WorldGen/StarUnlit) - enélkül a kvad éles szélű
                // NÉGYZETKÉNT jelenne meg (ld. felhasználói visszajelzés a
                // Nap-korongra), nem pontszerű/kör alakú fényforrásként.
                uvs[vBase + 0] = new Vector2(0f, 0f);
                uvs[vBase + 1] = new Vector2(1f, 0f);
                uvs[vBase + 2] = new Vector2(1f, 1f);
                uvs[vBase + 3] = new Vector2(0f, 1f);

                int tBase = i * 6;
                triangles[tBase + 0] = vBase + 0; triangles[tBase + 1] = vBase + 1; triangles[tBase + 2] = vBase + 2;
                triangles[tBase + 3] = vBase + 0; triangles[tBase + 4] = vBase + 2; triangles[tBase + 5] = vBase + 3;
            }

            var mesh = new Mesh { name = "StarField" };
            if (vertices.Length > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (starMaterial == null)
                starMaterial = CreateStarMaterial();
            mr.sharedMaterial = starMaterial;

            _builtForSeed = worldSeed;
            _hasBuilt = true;
        }

        private static Material CreateStarMaterial()
        {
            Shader shader = Shader.Find("WorldGen/StarUnlit");
            if (shader == null)
            {
                Debug.LogWarning("StarField: a \"WorldGen/StarUnlit\" shader nem található.");
                return null;
            }
            return new Material(shader);
        }
    }
}
