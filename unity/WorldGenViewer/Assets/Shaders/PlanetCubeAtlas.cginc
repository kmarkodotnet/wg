// KOZOS kockagomb-atlasz mintavetel es ket kis segedfuggveny.
//
// MIERT KULON FAJL. Ugyanezt a hat-lapos, laponkent 66x66 texeles atlaszt
// KETTO shader olvassa: a terep-shader (VertexColorUnlit - homerseklet-overlay
// es felhoarnyek) es a felho-raymarch (CloudVolume). Ket MASOLAT a
// koordinata-kepletbol pont az az ND-128 hibaosztaly, amit az egesz projekt
// kerul (ket, egymastol elcsuszo szamkeszlet) - ezert egy helyen van.
//
// A CPU-oldali par: WorldGen.Viewer.Lod.ThermalOverlayPacking.AtlasCoordinate
// (es ugyanazt a texel->cella leképezést hasznalja a CloudSkyAtlas is).
#ifndef WORLDGEN_PLANET_CUBE_ATLAS_INCLUDED
#define WORLDGEN_PLANET_CUBE_ATLAS_INCLUDED

// Biztonsagos normalize: nulla-kozeli (degeneralt) bemenetre a megadott
// tartalek-iranyt adja NaN helyett (HLSL normalize(0,0,0) = NaN, 0/0 miatt).
float3 PlanetSafeNormalize(float3 v, float3 fallback)
{
    float lenSq = dot(v, v);
    return lenSq > 1e-10 ? v * rsqrt(lenSq) : fallback;
}

// Kvintikus simitas a [0,1] savra - ugyanaz a fade-gorbe, mint a Core
// FractalNoise-ban es a SurfaceMicroDetail.Smoothstep01-ben.
float PlanetSmoothstep01(float t)
{
    t = saturate(t);
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

// Core-iranybol (Unity-lokal (x, z, y) tengelycserevel) atlasz-UV.
// A TileGeometry lapkonvenciojat es a tan-warp inverzet tukrozi.
float2 PlanetAtlasUv(float3 planetLocal)
{
    float3 u = normalize(planetLocal);
    float3 p = float3(u.x, u.z, u.y);
    float3 a = abs(p);
    int axis = 0;
    if (a.y > a.x) axis = 1;
    if (a.z > (axis == 0 ? a.x : a.y)) axis = 2;
    float dominant = axis == 0 ? p.x : (axis == 1 ? p.y : p.z);
    int face = axis * 2 + (dominant >= 0.0 ? 0 : 1);
    float inv = 1.0 / max(abs(dominant), 1e-6);
    float wx, wy;
    if (face == 0)      { wx = -p.z; wy = p.y; }
    else if (face == 1) { wx = p.z;  wy = p.y; }
    else if (face == 2) { wx = p.x;  wy = p.z; }
    else if (face == 3) { wx = p.x;  wy = -p.z; }
    else if (face == 4) { wx = p.x;  wy = p.y; }
    else                { wx = -p.x; wy = p.y; }
    float uc = atan(wx * inv) * 4.0 / UNITY_PI;
    float vc = atan(wy * inv) * 4.0 / UNITY_PI;
    float x = (66.0 * face + 1.0 + (uc + 1.0) * 32.0) / 396.0;
    float y = (1.0 + (vc + 1.0) * 32.0) / 66.0;
    return float2(x, y);
}

#endif
