using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer
{
    /// <summary>
    /// M2 vizuális cél: "szürke gömb, tile-határokkal" - a cubed sphere
    /// rács (src/WorldGen.Core/Grid) mesh-be építve, hogy a szomszédság és
    /// a vetítés vizuálisan is ellenőrizhető legyen.
    ///
    /// A geometria a WorldGen.Core-ból jön (TileGeometry.PositionFromFaceUV,
    /// GetContinuousBounds) - itt csak Unity Mesh-re fordítjuk, semmilyen
    /// rács-számítás nincs duplikálva.
    ///
    /// A `radius` egyelőre tetszőleges Unity-egység, NEM valós bolygóméret
    /// (7420 km) - a nagy-világ precíziós kérdés (ND-19, floating origin)
    /// külön lépés, mielőtt ez éles skálán futna.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class PlanetGridMesh : MonoBehaviour
    {
        [SerializeField, Range(0, 8)]
        [Tooltip("LOD-szint. Level 5-6 ajánlott első nézetre (6144-24576 tile).")]
        private int level = 5;

        [SerializeField]
        private float radius = 100f;

        [SerializeField]
        [Tooltip("A tile-határ vonalháló megjelenítése. Magas LOD-szinten (7+) " +
                 "sok ezer vékony vonal moiré-mintázatot ad a képernyőn - ott " +
                 "érdemes kikapcsolni, a szürke felület önmagában marad látható.")]
        private bool showBorders = true;

        [SerializeField]
        [Tooltip("Ha üres, egy alap fekete HDRP/Unlit anyagot hoz létre futásidőben.")]
        private Material borderMaterial;

        private void Start() => Build();

        [ContextMenu("Rebuild")]
        public void Build()
        {
            int n = 1 << level;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            var borderVerts = new List<Vector3>();
            var borderIndices = new List<int>();

            for (int face = 0; face <= 5; face++)
            {
                for (uint u = 0; u < n; u++)
                {
                    for (uint v = 0; v < n; v++)
                    {
                        TileId id = TileId.FromFaceLevelUV(face, level, u, v);
                        TileGeometry.GetContinuousBounds(id, out double uMin, out double uMax, out double vMin, out double vMax);

                        Vector3 p00 = ToVector3(face, uMin, vMin);
                        Vector3 p10 = ToVector3(face, uMax, vMin);
                        Vector3 p11 = ToVector3(face, uMax, vMax);
                        Vector3 p01 = ToVector3(face, uMin, vMax);

                        int baseIndex = vertices.Count;
                        vertices.Add(p00); vertices.Add(p10); vertices.Add(p11); vertices.Add(p01);

                        // Facetált normál (tile-onkénti lapos árnyékolás) - ez maga is
                        // vizuálisan kirajzolja a tile-határokat, a vonal-overlay mellett.
                        // A gömb középpontjától kifelé kell mutatnia - ez geometriailag
                        // garantált (a pozícióvektorral vett skalárszorzat előjeléből),
                        // nem a háromszög-bejárási iránytól függ.
                        Vector3 normal = Vector3.Cross(p10 - p00, p01 - p00).normalized;
                        if (Vector3.Dot(normal, p00) < 0f) normal = -normal;
                        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);

                        // A háromszög bejárási iránya (nem csak a normál-attribútum) dönti el
                        // Unity alatt, hogy a felület melyik oldala látszik (culling). A
                        // bejárás által implikált normált a fentebb már megbízhatóan kifelé
                        // irányított "normal"-hoz igazítjuk - ha eltérne, megfordítjuk a
                        // sorrendet, hogy minden lapon KONZISZTENSEN kifelé nézzen.
                        Vector3 impliedNormal1 = Vector3.Cross(p10 - p00, p11 - p00);
                        if (Vector3.Dot(impliedNormal1, normal) >= 0f)
                        {
                            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 2);
                            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 3);
                        }
                        else
                        {
                            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 1);
                            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 2);
                        }

                        int b = borderVerts.Count;
                        borderVerts.Add(p00); borderVerts.Add(p10); borderVerts.Add(p11); borderVerts.Add(p01);
                        borderIndices.Add(b + 0); borderIndices.Add(b + 1);
                        borderIndices.Add(b + 1); borderIndices.Add(b + 2);
                        borderIndices.Add(b + 2); borderIndices.Add(b + 3);
                        borderIndices.Add(b + 3); borderIndices.Add(b + 0);
                    }
                }
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;

            Transform borderChild = transform.Find("Borders");

            if (!showBorders)
            {
                if (borderChild != null) borderChild.gameObject.SetActive(false);
                return;
            }

            var borderMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            borderMesh.SetVertices(borderVerts);
            borderMesh.SetIndices(borderIndices, MeshTopology.Lines, 0);
            borderMesh.RecalculateBounds();

            GameObject borderGo;
            if (borderChild == null)
            {
                borderGo = new GameObject("Borders");
                borderGo.transform.SetParent(transform, false);
                borderGo.AddComponent<MeshFilter>();
                MeshRenderer mr = borderGo.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.sharedMaterial = borderMaterial != null ? borderMaterial : CreateDefaultBorderMaterial();
            }
            else
            {
                borderGo = borderChild.gameObject;
                borderGo.SetActive(true);
            }
            borderGo.GetComponent<MeshFilter>().sharedMesh = borderMesh;
        }

        private Vector3 ToVector3(int face, double uc, double vc)
        {
            TileGeometry.PositionFromFaceUV(face, uc, vc, out double x, out double y, out double z);
            return new Vector3((float)x, (float)y, (float)z) * radius;
        }

        private static Material CreateDefaultBorderMaterial()
        {
            Shader shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("PlanetGridMesh: a \"HDRP/Unlit\" shader nem található - " +
                                  "állíts be kézzel egy borderMaterial-t az Inspectorban.");
                return null; // Unity a beépített rózsaszín default anyagot adja - jól látható jelzés
            }
            var mat = new Material(shader);
            mat.color = Color.black;
            return mat;
        }
    }
}
