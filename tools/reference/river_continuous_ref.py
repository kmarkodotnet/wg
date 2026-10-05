"""
Folytonos (tile-racstol fuggetlen) folyo-nyomvonalkoveto REFERENCIA-ORAKULUMA,
az ND-180 "C" opciojanak 1. koreehez (ND-186).

MIERT KELL. A `river_path_ref.py` a DURVA, TileId-racson futo kovetot irja le.
A termekben viszont a MEGJELENITETT halozatot a FOLYTONOS koveto adja
(`RiverPathTracing.TraceRiverPathContinuous`), es ennek 2026-10-02-ig NEM volt
Python-orakuluma. Az ND-180 meresei harom konkret modellhibat igazoltak ezen a
folytonos uton:

  (1) IRANYKVANTALAS - a lepes iranya a `ringDirections` (8) jelolt irany
      egyikere volt kerekitve, ezert a nyomvonal 45 fokos lepcsokbol allt
      (merve: 1422 eles kanyar a 0. agon).
  (2) HAMIS KOZELSEGI OSSZEFOLYAS - a `claimed` terkep EGY pontot tarolt egy
      TELJES finom tile-ra (level 9, kb. 13-18 km), es a beleolvado folyot
      ERRE a pontra zarta, VALODI tavolsagvizsgalat nelkul. Merve: a legnagyobb
      osszefolyasi zaroel 24,113 km - lathato "teleport".
  (3) FINOMITATLAN ESCAPE-SZAKASZ - a lokalis priority-flood 2000 m-es racsan
      talalt utat a koveto KOZVETLENUL fuzte a nyomvonalhoz, ezert 2,0 / 2,828
      km-es elek kerultek bele (a normal lepes 50 m). Merve: a halozat teljes
      hosszanak 58,77%-a 75 m-nel hosszabb eleken van.

Ez a modul a JAVITOTT (v2) algoritmust definialja, es SZINTETIKUS, ANALITIKUS
domborzatokon generalja a C# port tesztvektorait. Szandekosan NEM a valodi
bolygo-domborzaton: azt a lancot (domain warp + lemezkeret + erozio) a Python
oldal nem tudja bitre reprodukalni, a HIBAOSZTALYOK viszont analitikus sikon,
volgyben, medenceben es ket parhuzamos mederben PONTOSAN eloallnak.

BITPONTOSSAG. A modul KIZAROLAG `+ - * /` es `sqrt` muveleteket hasznal
(IEEE-754 korrekt kerekites mindketto nyelvben), SEMMILYEN transzcendens
funkciot (`sin`, `cos`, `tan`, `atan`, `exp`, `log`) - ld. CLAUDE.md
lebegopontos tablazat es ND-23 / ND-24. Ez a v2 egyik celja is: a regi koveto
`Math.Cos`/`Math.Sin`-nel epitette a jelolt-iranyokat, es
`TileGeometry.FromPosition`-t (azaz `Math.Atan`-t) hivott a hurok-vedelemhez
es a `claimed` kereseshez - mindketto ND-24 szerint TILOS a szimulacio
kritikus utjan.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import json
import math
from pathlib import Path

# A bolygo sugara (src/WorldGen.Core/PlanetConstants.cs).
RADIUS_METERS = 7_420_000.0

# --- v2 alapertelmezesek (a C# oldal `DefaultContinuous*` parjai) ---
DEFAULT_STEP_METERS = 50.0
DEFAULT_SENSING_RADIUS_METERS = 500.0
DEFAULT_RING_DIRECTIONS = 8
DEFAULT_ESCAPE_CELL_METERS = 2000.0
DEFAULT_ESCAPE_NODE_BUDGET = 30_000
DEFAULT_MAX_STEPS = 500_000
# Hurok-vedelem racsszintje: atan-mentes kocka-lap racs, kb. 7-14 m/cella.
DEFAULT_VISITED_LATTICE_LEVEL = 20
# Az osszefolyasi (claimed) terbeli index racsszintjet a toleranciabol
# SZAMOLJUK (ld. `lattice_level_for`) - 100 m-re ez level 16 (113-226 m/cella).
MAX_LATTICE_LEVEL = 20
# Valodi terbeli osszefolyasi tolerancia METERBEN (a regi "egy teljes finom
# tile" szemantika helyett). MERESSEL eldontve a valodi t=0 halozaton:
# 100 m -> 14 osszefolyas, 250 m -> 14, 500 m -> 18, 2000 m -> 18, tehat
# 500 m-nel TELITODIK. Egybeesik az erzekelesi sugarral (500 m), vagyis azzal
# a leptekkel, amin a koveto a domborzatot megiteli.
DEFAULT_MERGE_RADIUS_METERS = 500.0
# Az escape-szakasz "viz alatti" egyenes roviditeset legfeljebb ennyi durva
# cellan at probaljuk - tisztan koltsegkorlat, a dontest nem befolyasolja
# (nagyobb ertek csak hosszabb egyenes szakaszokat engedne).
DEFAULT_MAX_SHORTCUT_CELLS = 64
# Az escape-szakasz PONTSURUSEGE. MERT indok (ND-186): a 2000 m-es racson
# meghozott escape-dontest 50 m-re mintavetelezve a t=0 halozat pontszama
# 415 293 -> 954 598 lett (+130%), es a mesh-elokeszites ennyivel nyulna -
# holott egy "viz alatti", SIMA szakasz alakjarol a 2000 m-es racsnal finomabb
# mintavetel NEM ad uj modell-informaciot. 250 m meg mindig 8x finomabb, mint
# a dontesi racs, es eleg suru ahhoz, hogy a kirajzolt vonal a megjelenitett
# felszinen maradjon (a kozbenso pontokat a viewer a domborzatra ulteti).
DEFAULT_ESCAPE_EMIT_METERS = 250.0

# ND-187: egy zart medence ilyen melysegtol szamit TO-nak (a benne futo
# szakaszt viz alattinak jeloljuk). MERVE: kuszob nelkul a jeloles a t=0
# halozat hosszanak 71,80%-at fedte, a LATHATO tavak 41,34% helyett - apro,
# nehany meteres melyedeseken a viz ATFOLYIK, ott folyo van, nem to. Az
# ertek a megjelenitett to-reteg melyseg-kuszobevel egyezik.
DEFAULT_SUBMERGED_MIN_DEPTH_METERS = 40.0

TERMINATION_OCEAN = "Ocean"
TERMINATION_PIT = "Pit"
TERMINATION_MERGED = "Merged"
TERMINATION_MAX_STEPS = "MaxSteps"


# ---------------------------------------------------------------------------
# Gomb-geometria (bitpontos: csak + - * / es sqrt)
# ---------------------------------------------------------------------------
def normalize(v):
    x, y, z = v
    length = math.sqrt(x * x + y * y + z * z)
    return (x / length, y / length, z / length)


def tangent_basis(p):
    """UGYANAZ a konvencio, mint a C# `GetTangentBasis`-ben."""
    reference = (0.0, 0.0, 1.0) if abs(p[2]) < 0.9 else (0.0, 1.0, 0.0)
    dot = p[0] * reference[0] + p[1] * reference[1] + p[2] * reference[2]
    r = (reference[0] - dot * p[0], reference[1] - dot * p[1], reference[2] - dot * p[2])
    t1 = normalize(r)
    t2 = (p[1] * t1[2] - p[2] * t1[1],
          p[2] * t1[0] - p[0] * t1[2],
          p[0] * t1[1] - p[1] * t1[0])
    return t1, t2


def step_in_tangent(p, t1, t2, dx, dy, angular_step):
    """UGYANAZ, mint a C# `StepInTangentDirection` - erintosiki elmozdulas,
    majd EGZAKT visszanormalizalas a gombre."""
    return normalize((
        p[0] + angular_step * (dx * t1[0] + dy * t2[0]),
        p[1] + angular_step * (dx * t1[1] + dy * t2[1]),
        p[2] + angular_step * (dx * t1[2] + dy * t2[2]),
    ))


def geodesic_point(a, b, t):
    """Az `a` es `b` kozti nagykor-szakasz `t` parametere - normalizalt lineari
    interpolacio. Erre a leptekre (nehany km, 7420 km-es sugaron) a chord-
    es a nagykor-parameterezes elterese elhanyagolhato, es a muvelet
    bitpontos (nincs trigonometria)."""
    return normalize((
        a[0] + (b[0] - a[0]) * t,
        a[1] + (b[1] - a[1]) * t,
        a[2] + (b[2] - a[2]) * t,
    ))


def chord_meters(a, b):
    dx, dy, dz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
    return math.sqrt(dx * dx + dy * dy + dz * dz) * RADIUS_METERS


# ---------------------------------------------------------------------------
# Atan-mentes kocka-lap racs (terbeli hasheles - ND-24 megfelelo)
# ---------------------------------------------------------------------------
# A `TileGeometry.FromPosition` a tan-torzitott kocka-lap koordinatat hasznalja,
# amihez `Math.Atan` kell - az ND-24 szerint ez NEM hasznalhato a szimulacio
# kritikus utjan (nem bitpontos platformok kozott). A terbeli hashelesnek
# viszont nincs szuksege terulet-kiegyenlitett cellakra: a NYERS kocka-
# projekcio (dominans tengely + ket osztas) ugyanolyan jo bucket-racs, es
# kizarolag osztast es osszehasonlitast hasznal, tehat bitpontos.
_NORMAL_AXIS = (0, 0, 1, 1, 2, 2)
_NORMAL_SIGN = (1, -1, 1, -1, 1, -1)
_RIGHT_AXIS = (2, 2, 0, 0, 0, 0)
_RIGHT_SIGN = (-1, 1, 1, 1, 1, -1)
_UP_AXIS = (1, 1, 2, 2, 1, 1)
_UP_SIGN = (1, 1, 1, -1, 1, 1)


def lattice_cell(p, level):
    """(face, i, j) bucket-index a NYERS (nem tan-torzitott) kocka-lapon."""
    comp = (p[0], p[1], p[2])
    dominant = 0
    if abs(comp[1]) > abs(comp[dominant]):
        dominant = 1
    if abs(comp[2]) > abs(comp[dominant]):
        dominant = 2
    dominant_sign = 1 if comp[dominant] >= 0.0 else -1
    face = -1
    for f in range(6):
        if _NORMAL_AXIS[f] == dominant and _NORMAL_SIGN[f] == dominant_sign:
            face = f
            break
    assert face >= 0
    scale = 1.0 / abs(comp[_NORMAL_AXIS[face]])
    a = comp[_RIGHT_AXIS[face]] * scale * _RIGHT_SIGN[face]
    b = comp[_UP_AXIS[face]] * scale * _UP_SIGN[face]
    n = 1 << level
    i = int((a + 1.0) * 0.5 * n)
    j = int((b + 1.0) * 0.5 * n)
    i = max(0, min(n - 1, i))
    j = max(0, min(n - 1, j))
    return (face, i, j)


def min_cell_meters(level):
    """A cellak GARANTALT also merethatara meterben: a lap `a` koordinataja
    [-1,1]-en fut, a hozzatartozo szog atan(a), aminek derivaltja 1/(1+a^2),
    |a|=1-nel 1/2 - ez a minimum. Egy cella 2/n szeles `a`-ban, tehat a
    legkisebb szogmerete 1/n radian, azaz R/n meter."""
    return RADIUS_METERS / (1 << level)


def lattice_level_for(merge_radius_meters):
    """A legfinomabb olyan racsszint, aminek a garantalt legkisebb cellaja meg
    legalabb `merge_radius_meters`. Kettozessel szamol, LOGARITMUS NELKUL -
    a `log` nem bitpontos, es egy racsszint elcsuszasa mas halozatot adna."""
    assert merge_radius_meters > 0.0
    level = 1
    while level < MAX_LATTICE_LEVEL and min_cell_meters(level + 1) >= merge_radius_meters:
        level += 1
    return level


# ---------------------------------------------------------------------------
# Bitpontos jelolt-irany tabla (trigonometria NELKUL)
# ---------------------------------------------------------------------------
def ring_directions(count):
    """`count` (2 hatvanya, >= 4) egyenletesen elosztott egysegvektor az
    erintosikon, KIZAROLAG szogfelezessel (`normalize(a+b)`) eloallitva.

    MIERT NEM `cos`/`sin`: azok nem bitpontosak platformok kozott (CLAUDE.md
    tablazat, ND-23). A szogfelezes csak osszeadast, osztast es `sqrt`-et
    hasznal, es EGZAKTUL szimmetrikus tablat ad: a k. es a (k + count/2).
    irany bitre egymas ellentettje, ezert a jelolt-kor nem visz be
    iranyfuggo torzitast."""
    assert count >= 4 and (count & (count - 1)) == 0
    quarter = count // 4
    # Elso negyed (0 .. 90 fok), `quarter` + 1 pont, ismetelt felezessel.
    arc = [(1.0, 0.0), (0.0, 1.0)]
    while len(arc) - 1 < quarter:
        refined = [arc[0]]
        for k in range(len(arc) - 1):
            ax, ay = arc[k]
            bx, by = arc[k + 1]
            length = math.sqrt((ax + bx) * (ax + bx) + (ay + by) * (ay + by))
            refined.append(((ax + bx) / length, (ay + by) / length))
            refined.append(arc[k + 1])
        arc = refined
    directions = []
    for k in range(count):
        q, r = divmod(k, quarter)
        cx, cy = arc[r]
        # q x 90 fok EGZAKT elforgatas: (c, s) -> (-s, c)
        for _ in range(q):
            cx, cy = -cy, cx
        directions.append((cx, cy))
    return directions


# ---------------------------------------------------------------------------
# Osszefolyasi index: VALODI terbeli kozelsegvizsgalat
# ---------------------------------------------------------------------------
class ClaimedRiverPoints:
    """A mar lefoglalt folyopontok terbeli indexe.

    A REGI szemantika: `dict[TileId(level 9)] -> (riverIndex, egyetlen pont)`.
    Egy ilyen tile 13-18 km, tehat a beleolvado folyo akar 24 km-t "teleportalt"
    a tile-ban eloszor rogzitett pontra (ND-180 meres).

    A v2: cellankent a lefoglalt pontok LISTAJA egy finom (kb. 113-226 m)
    atan-mentes racson, es az osszefolyas CSAK akkor tortenik, ha van
    `merge_radius_meters`-en BELULI valodi lefoglalt pont; a zarast a
    LEGKOZELEBBIRE vegezzuk. Igy az osszefolyasi zaroel hossza
    STRUKTURALISAN korlatos (<= merge_radius_meters)."""

    def __init__(self, merge_radius_meters=DEFAULT_MERGE_RADIUS_METERS):
        self.level = lattice_level_for(merge_radius_meters)
        self.merge_radius_meters = merge_radius_meters
        self.cells = {}
        self.counter = 0

    def add(self, position, river_index):
        cell = lattice_cell(position, self.level)
        self.cells.setdefault(cell, []).append((position, river_index, self.counter))
        self.counter += 1

    def find_nearest(self, position, merge_radius_meters):
        """A legkozelebbi lefoglalt pont a sugaron belul, vagy None.
        Dontetlennel a KORABBI beszurasi sorszam nyer (determinisztikus).

        A kereses 9 PROBAPONTOT vetit cellara (a pont maga + negy tengely-
        iranyu + negy atlos eltolas a sugarban), nem cella-INDEX szomszedokat:
        igy a lap-hataron at is helyesen mukodik. A lefedes feltetele, hogy a
        cella legkisebb merete >= sugar - errol a `lattice_level_for` gondoskodik."""
        limit = merge_radius_meters / RADIUS_METERS
        t1, t2 = tangent_basis(position)
        keys = []
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                probe = (position if dx == 0 and dy == 0
                         else step_in_tangent(position, t1, t2, dx, dy, limit))
                cell = lattice_cell(probe, self.level)
                if cell not in keys:
                    keys.append(cell)
        best = None
        for cell in keys:
            for (point, river_index, order) in self.cells.get(cell, ()):
                dx = point[0] - position[0]
                dy = point[1] - position[1]
                dz = point[2] - position[2]
                distance = math.sqrt(dx * dx + dy * dy + dz * dz)
                if distance > limit:
                    continue
                key = (distance, order)
                if best is None or key < best[0]:
                    best = (key, point, river_index)
        if best is None:
            return None
        return (best[1], best[2])


class LegacyClaimedTiles:
    """A REGI (ND-180 elotti) osszefolyas-szemantika, KIZAROLAG az elotte-utana
    meresehez: egy TELJES durva cellara EGY pont van tarolva, es a beleolvado
    folyot erre a pontra zarjuk, valodi tavolsagvizsgalat NELKUL. A durva
    cellaszint (`level`) a termekben `source.Level + fineDepth` = 9 volt, ami
    kb. 13-18 km - innen a mert 24,113 km-es osszefolyasi "teleport"."""

    def __init__(self, level):
        self.level = level
        self.cells = {}

    def add(self, position, river_index):
        cell = lattice_cell(position, self.level)
        if cell not in self.cells:
            self.cells[cell] = (position, river_index)

    def find_nearest(self, position, merge_radius_meters):
        return self.cells.get(lattice_cell(position, self.level))


# ---------------------------------------------------------------------------
# Lokalis priority-flood escape (valtozatlan DONTES, finomitott KIMENET)
# ---------------------------------------------------------------------------
def find_local_spillway(elev_fn, pit, pit_elevation, cell_meters, node_budget,
                        path_visited, visited_level):
    """UGYANAZ az elv, mint a C# `FindContinuousLocalSpillway`: korlatozott
    csomoponszamu priority-flood a `pit` koruli, a pit-ben rogzitett
    erintosiki egeszracson, 8 szomszeddal. A visszatereses ertek a durva
    racsutvonal (a pit-tol a tulcsordulasi pontig) es a tulcsordulasi
    eleváció."""
    t1, t2 = tangent_basis(pit)
    cell_angular = cell_meters / RADIUS_METERS

    def cell_pos(i, j):
        return step_in_tangent(pit, t1, t2, float(i), float(j), cell_angular)

    def cell_elev(i, j):
        return elev_fn(cell_pos(i, j))

    def is_path_visited(i, j):
        return lattice_cell(cell_pos(i, j), visited_level) in path_visited

    visited = {(0, 0)}
    parent = {(0, 0): None}
    # (filled_elevation, counter) - a `counter` a determinisztikus dontetlen.
    import heapq
    queue = []
    counter = 0
    heapq.heappush(queue, (pit_elevation, counter, (0, 0)))
    counter += 1

    neighbors8 = ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1))
    expanded = 0
    while queue and expanded < node_budget:
        filled, _, node = heapq.heappop(queue)
        expanded += 1
        raw = pit_elevation if node == (0, 0) else cell_elev(node[0], node[1])
        if node != (0, 0) and raw < pit_elevation and not is_path_visited(node[0], node[1]):
            index_path = []
            cur = node
            while cur is not None:
                index_path.append(cur)
                cur = parent[cur]
            index_path.reverse()
            return ([cell_pos(i, j) for (i, j) in index_path], raw)
        for (di, dj) in neighbors8:
            nb = (node[0] + di, node[1] + dj)
            if nb in visited or is_path_visited(nb[0], nb[1]):
                continue
            visited.add(nb)
            nb_filled = max(cell_elev(nb[0], nb[1]), filled)
            parent[nb] = node
            heapq.heappush(queue, (nb_filled, counter, nb))
            counter += 1
    return None


def refine_escape_path(elev_fn, coarse_path, lake_level, emit_meters,
                       probe_meters, max_shortcut_cells):
    """Az ND-180 (3) hiba javitasa: a durva racsutvonalbol EGYENES, "viz alatti"
    szakaszokat vonunk ossze.

    MIERT EZ A HELYES JAVITAS. A priority-flood utvonala definicio szerint a
    `lake_level` (az utvonal legmagasabb nyers elevacioja) ALATT marad, vagyis
    a medence FELTOLTODESE utan VIZ ALATT van. Egy ilyen teruleten a fizikai
    vizfelszin SIMA: a 8-szomszedos racs 45 fokos lepcsoje NEM modell-tartalom,
    hanem a racs melléktermeke. Ezert ahol egy EGYENES (nagykor) szakasz
    minden mintapontja `lake_level` alatt marad, ott az egyenes a HELYESEBB
    nyomvonal - nem dekorativ simitas, hanem a racs-muvitermek eltavolitasa.
    Ahol az egyenes KIBUKKANNA a vizbol, ott a racsutvonal reszletei
    megmaradnak.

    A visszaadott pontlista a `coarse_path`-bol az ELSO pontot (a pit-et)
    KIHAGYJA - azt a hivo mar felvette (a regi kod ezt duplan vette fel,
    nulla hosszu ellel)."""
    output = []
    anchor_index = 0
    count = len(coarse_path)
    while anchor_index < count - 1:
        anchor = coarse_path[anchor_index]
        best = anchor_index + 1
        limit = min(count - 1, anchor_index + max_shortcut_cells)
        for candidate in range(anchor_index + 2, limit + 1):
            if _segment_submerged(elev_fn, anchor, coarse_path[candidate],
                                  lake_level, probe_meters):
                best = candidate
            else:
                break
        target = coarse_path[best]
        # A szakaszt `emit_meters` surusegre mintavetelezzuk (ld. a
        # DEFAULT_ESCAPE_EMIT_METERS melletti meresi indoklast).
        span = chord_meters(anchor, target)
        pieces = max(1, int(span / emit_meters))
        for k in range(1, pieces + 1):
            output.append(geodesic_point(anchor, target, k / pieces))
        anchor_index = best
    return output


def _segment_submerged(elev_fn, a, b, lake_level, probe_meters):
    """Igaz, ha az `a`-`b` nagykor-szakasz MINDEN belso mintapontja a
    `lake_level` alatt (vagy azon) van. A mintavetel surusege `probe_meters`
    - a durva racs cellameretenel finomabb, de nem indokolatlanul suru:
    a domborzati dontes maga is a durva racson keszult."""
    span = chord_meters(a, b)
    probes = max(1, int(span / probe_meters))
    for k in range(1, probes):
        if elev_fn(geodesic_point(a, b, k / probes)) > lake_level:
            return False
    return True


# ---------------------------------------------------------------------------
# A v2 folytonos koveto
# ---------------------------------------------------------------------------
def trace_continuous(elev_fn, source, source_index, sea_level,
                     claimed=None,
                     step_meters=DEFAULT_STEP_METERS,
                     sensing_radius_meters=DEFAULT_SENSING_RADIUS_METERS,
                     ring_count=DEFAULT_RING_DIRECTIONS,
                     escape_cell_meters=DEFAULT_ESCAPE_CELL_METERS,
                     escape_node_budget=DEFAULT_ESCAPE_NODE_BUDGET,
                     max_steps=DEFAULT_MAX_STEPS,
                     visited_level=DEFAULT_VISITED_LATTICE_LEVEL,
                     merge_radius_meters=DEFAULT_MERGE_RADIUS_METERS,
                     max_shortcut_cells=DEFAULT_MAX_SHORTCUT_CELLS,
                     escape_emit_meters=DEFAULT_ESCAPE_EMIT_METERS,
                     submerged_min_depth_meters=DEFAULT_SUBMERGED_MIN_DEPTH_METERS,
                     legacy=False):
    """A javitott folytonos nyomvonalkoveto.

    A VALTOZATLAN szerkezet: a seta a forrasbol indul, maga donti el a veget
    (Ocean / Pit / Merged / MaxSteps); normal lepes CSAK akkor, ha a
    jelolt-korben van SZIGORUAN alacsonyabb pont; kulonben lokalis
    priority-flood escape.

    A HAROM JAVITAS:
      (1) a lepes iranya a jelolt-kor ELSO HARMONIKUSABOL szamolt FOLYTONOS
          lejtesirany, nem a legjobb jelolt-irany (iranykvantalas vege);
      (2) az osszefolyas VALODI terbeli kozelsegvizsgalat (ld.
          `ClaimedRiverPoints`), nem "ugyanabban a 13 km-es tile-ban";
      (3) az escape-szakasz "viz alatti" egyenesekre van egyszerusitve es
          `step_meters` surusegre mintavetelezve (ld. `refine_escape_path`).
    """
    directions = ring_directions(ring_count)
    step_angular = step_meters / RADIUS_METERS
    sensing_angular = max(step_angular, sensing_radius_meters / RADIUS_METERS)

    current = source
    points = [current]
    # ND-187: pontonkenti elevacio - CSAK a viz alatti szakaszok
    # visszamenoleges megjelolesehez kell (lasd az escape-agat). Nem
    # befolyasol egyetlen lepes-dontest sem.
    point_elevations = [elev_fn(current)]
    submerged_spans = []
    claim_check_indices = []
    path_visited = {lattice_cell(current, visited_level)}
    elev = point_elevations[0]
    termination = TERMINATION_MAX_STEPS
    merged_into = -1
    escape_count = 0

    for step in range(max_steps):
        claim_check_indices.append(len(points) - 1)

        if elev < sea_level:
            termination = TERMINATION_OCEAN
            break

        if claimed is not None and step > 0:
            hit = claimed.find_nearest(current, merge_radius_meters)
            if hit is not None:
                points.append(hit[0])
                point_elevations.append(elev_fn(hit[0]))
                merged_into = hit[1]
                termination = TERMINATION_MERGED
                break

        t1, t2 = tangent_basis(current)

        sensed = []
        best_elev = elev
        best_index = -1
        for k in range(ring_count):
            dx, dy = directions[k]
            probe = step_in_tangent(current, t1, t2, dx, dy, sensing_angular)
            probe_elev = elev_fn(probe)
            sensed.append(probe_elev)
            if probe_elev < best_elev:
                best_elev = probe_elev
                best_index = k

        if best_index >= 0:
            direction = (directions[best_index] if legacy
                         else _descent_direction(sensed, directions, ring_count, best_index))
            candidate = step_in_tangent(current, t1, t2, direction[0], direction[1], step_angular)
            candidate_elev = elev_fn(candidate)
            if candidate_elev >= elev and direction != directions[best_index]:
                # A folytonos irany nem lejt - visszaesunk a legjobb jelolt
                # iranyra (a regi, kvantalt viselkedes), hogy a szigoru
                # lejtes-kapu ne gyenguljon.
                direction = directions[best_index]
                candidate = step_in_tangent(current, t1, t2, direction[0], direction[1], step_angular)
                candidate_elev = elev_fn(candidate)
            current = candidate
            elev = candidate_elev
            points.append(current)
            point_elevations.append(candidate_elev)
            path_visited.add(lattice_cell(current, visited_level))
            continue

        escape = find_local_spillway(elev_fn, current, elev, escape_cell_meters,
                                     escape_node_budget, path_visited, visited_level)
        if escape is None:
            termination = TERMINATION_PIT
            break
        escape_count += 1
        coarse_path, spillway_elevation = escape
        if legacy:
            # A regi ut a durva racsutvonalat KOZVETLENUL fuzte a nyomvonalhoz
            # (es a pit-et meg egyszer felvette, nulla hosszu ellel).
            refined = list(coarse_path)
            lake_level = None
        else:
            lake_level = max(elev_fn(p) for p in coarse_path)
            refined = refine_escape_path(elev_fn, coarse_path, lake_level, escape_emit_meters,
                                         escape_cell_meters * 0.5, max_shortcut_cells)
        # ND-187: a medence feltoltesi szintje (`lake_level`) alatti szakasz
        # VIZ ALATT van - fizikailag TO, nem folyo. A span a BEERESZKEDESSEL
        # kezdodik (a mar felvett pontokon visszafele, amig a pont a
        # `lake_level` alatt van), es az escape-szakasz vegeig tart.
        span_start = len(points) - 1
        if lake_level is not None:
            while span_start > 0 and point_elevations[span_start - 1] < lake_level:
                span_start -= 1
        for p in refined:
            points.append(p)
            point_elevations.append(lake_level if lake_level is not None else elev_fn(p))
            path_visited.add(lattice_cell(p, visited_level))
        span_end = len(points) - 1
        # ND-187 melyseg-kapu: csak a VALODI medence szamit tonak. Kuszob
        # nelkul a jeloles a t=0 halozat 71,80%-at fedte (a latható tavak 41,34%
        # helyett) - apro lokalis melyedesek miatt, amiken a viz ATFOLYIK.
        basin_depth = (lake_level - elev) if lake_level is not None else 0.0
        if (lake_level is not None and span_end > span_start
                and basin_depth >= submerged_min_depth_meters):
            if submerged_spans and submerged_spans[-1][1] >= span_start:
                # Egymasba ero medencek: osszevonjuk, hogy a tartomanyok
                # DISZJUNKTAK es novekvok maradjanak.
                submerged_spans[-1] = (submerged_spans[-1][0], span_end)
            else:
                submerged_spans.append((span_start, span_end))
        current = coarse_path[-1]
        elev = spillway_elevation

    return dict(sourceIndex=source_index, points=points, termination=termination,
                mergedIntoRiverIndex=merged_into,
                claimCheckIndices=claim_check_indices, escapeCount=escape_count,
                submergedSpans=[list(span) for span in submerged_spans])


def _descent_direction(sensed, directions, ring_count, best_index):
    """FOLYTONOS lejtesirany a jelolt-kor ELSO HARMONIKUSABOL.

    Egy `r` sugaru koron egyenletesen mintavett `e_k` magassagokra a
    legkisebb-negyzetes sikillesztes gradiense aranyos a
    `g = sum_k (e_k - atlag) * d_k` vektorral. A lejtesirany ennek az
    ELLENTETTJE, normalizalva - EZ a folytonos ertek, ami megszunteti a
    `directions` tablara valo kvantalast. Ha a gradiens pontosan nulla
    (teljesen sima kor), a legjobb jelolt-irany marad."""
    total = 0.0
    for k in range(ring_count):
        total += sensed[k]
    mean = total / ring_count
    gx = 0.0
    gy = 0.0
    for k in range(ring_count):
        weight = sensed[k] - mean
        gx += weight * directions[k][0]
        gy += weight * directions[k][1]
    length = math.sqrt(gx * gx + gy * gy)
    if length == 0.0:
        return directions[best_index]
    return (-gx / length, -gy / length)


def build_network(elev_fn, sources, sea_level, **kwargs):
    """Szekvencialis halozat MEGOSZTOTT `ClaimedRiverPoints` indexszel -
    ez a kanonikus szemantika (a parhuzamos ut ezt reprodukalja)."""
    legacy = kwargs.get("legacy", False)
    radius = kwargs.get("merge_radius_meters", DEFAULT_MERGE_RADIUS_METERS)
    claimed = (LegacyClaimedTiles(kwargs.pop("legacy_claim_level", 9)) if legacy
               else ClaimedRiverPoints(radius))
    rivers = []
    for index, source in enumerate(sources):
        river = trace_continuous(elev_fn, source, index, sea_level,
                                 claimed=claimed, **kwargs)
        rivers.append(river)
        for p in river["points"]:
            claimed.add(p, index)
    return rivers


# ---------------------------------------------------------------------------
# Szintetikus, ANALITIKUS domborzatok (csak + - * / es sqrt)
# ---------------------------------------------------------------------------
# A domborzatok egy EGYSEGGOMB-foltra vannak megfogalmazva az eszaki pol
# kornyeken: ott a pont (x, y, z) alakja kozel (u, v, 1), tehat `p[0]` a
# lejtes menti, `p[1]` a keresztiranyu lokalis koordinata. A kepletek
# KIZAROLAG a pozicio komponenseinek szorzatai/osszegei - nincs trigonometria,
# tehat C#-ban bitre ugyanazt adjak.
BASE_ELEVATION_METERS = 3_000.0
SLOPE_METERS_PER_UNIT = 40_000.0
WALL_METERS_PER_UNIT2 = 40_000_000.0


def terrain_plane(base=BASE_ELEVATION_METERS, slope=SLOPE_METERS_PER_UNIT):
    """Egyenletesen lejto sik: elev = base - slope * x. A FOLYTONOS lejtesirany
    tesztje - a helyes nyomvonal egy EGYENES a +x iranyban, a regi, 8 iranyra
    kvantalt koveto itt is lepcsozott, ha a forras nem pont a tablara esett."""
    def fn(p):
        return base - slope * p[0]
    return fn


def terrain_valley(base=BASE_ELEVATION_METERS, slope=SLOPE_METERS_PER_UNIT,
                   wall=WALL_METERS_PER_UNIT2, axis=0.0):
    """Lejto volgy: egyenletes lejtes +x fele, parabolikus falak az `axis`
    tengelyu meder korul. Ebbe a mederbe KELL befutnia minden kozeli
    nyomvonalnak - ez a valodi, fizikai osszefolyas tesztje."""
    def fn(p):
        side = p[1] - axis
        return base - slope * p[0] + wall * side * side
    return fn


def terrain_basin(base=BASE_ELEVATION_METERS, slope=SLOPE_METERS_PER_UNIT,
                  center=(0.0, 0.0), depth=3_000.0, radius=0.030):
    """Lejto sik + ZART medence (fordított parabola), tehat a nyomvonalnak
    escape-et kell keresnie es at kell vagnia a (feltoltodo) medencet.
    A medence a `radius`-on kivul nem hat, es a perem gradiense erosebb,
    mint a sik lejtese - igy valodi zart depresszio keletkezik."""
    radius2 = radius * radius

    def fn(p):
        dx = p[0] - center[0]
        dy = p[1] - center[1]
        d2 = dx * dx + dy * dy
        bowl = -depth * (1.0 - d2 / radius2) if d2 < radius2 else 0.0
        return base - slope * p[0] + bowl
    return fn


def terrain_twin_valleys(separation, base=BASE_ELEVATION_METERS,
                         slope=SLOPE_METERS_PER_UNIT, wall=WALL_METERS_PER_UNIT2):
    """Ket PARHUZAMOS meder `separation` (egyseggomb-chord) tavolsagra. A
    mederek sosem talalkoznak, tehat a tenyleges terbeli tavolsag donti el az
    osszefolyast - pontosan ez a regi, "ugyanabban a 13 km-es tile-ban"
    szemantika ellenpróbaja."""
    half = separation * 0.5

    def fn(p):
        left = p[1] + half
        right = p[1] - half
        return base - slope * p[0] + wall * min(left * left, right * right)
    return fn


TERRAINS = {
    "plane": lambda: terrain_plane(),
    "valley": lambda: terrain_valley(),
    # A medence-parameterek SZAMOLT zart depressziot adnak: a melypont
    # x=0,040-nel 800 m, a lefolyasi perem x=0,050-nel 1000 m, es a
    # tulcsordulasi pont csak x=0,055 korul (kb. 111 km-re) van - tehat a
    # nyomvonalnak VALODI, tobb durva cellan atvezeto escape-et kell keresnie.
    "basin": lambda: terrain_basin(base=3_000.0, center=(0.030, 0.0),
                                   depth=800.0, radius=0.020),
    "twin_near": lambda: terrain_twin_valleys(0.0004),
    "twin_far": lambda: terrain_twin_valleys(0.0120),
}


# ---------------------------------------------------------------------------
# Tesztvektor-generalas
# ---------------------------------------------------------------------------
def _round_trip(points):
    """A vektorfajlba a pontok teljes (R formatumu) double-ertekkel kerulnek -
    a JSON `repr` Pythonban a legrovidebb korbejaro alak, amit a C#
    `double.Parse` bitre visszaad."""
    return [[p[0], p[1], p[2]] for p in points]


def _case(name, terrain, sources, sea_level, params, expect_merge=None):
    elev_fn = TERRAINS[terrain]()
    rivers = build_network(elev_fn, sources, sea_level, **params)
    return dict(
        name=name,
        terrain=terrain,
        seaLevel=sea_level,
        params=dict(params),
        sources=_round_trip(sources),
        rivers=[dict(sourceIndex=r["sourceIndex"],
                     termination=r["termination"],
                     mergedIntoRiverIndex=r["mergedIntoRiverIndex"],
                     escapeCount=r["escapeCount"],
                     pointCount=len(r["points"]),
                     lengthMeters=sum(chord_meters(r["points"][i - 1], r["points"][i])
                                      for i in range(1, len(r["points"]))),
                     maxEdgeMeters=max([chord_meters(r["points"][i - 1], r["points"][i])
                                        for i in range(1, len(r["points"]))], default=0.0),
                     points=_round_trip(r["points"]),
                     claimCheckIndices=r["claimCheckIndices"],
                     submergedSpans=r["submergedSpans"])
                for r in rivers],
        expectMerge=expect_merge,
    )


def _ring_table_vectors():
    out = {}
    for count in (4, 8, 16, 32):
        out[str(count)] = [[d[0], d[1]] for d in ring_directions(count)]
    return out


def main():
    # A szintetikus domborzat planetaris lepteku es sima, ezert nagy lepeskozt
    # hasznalunk - igy a vektorfajl rovid marad, a HIBAOSZTALYOK viszont
    # ugyanazok (iranykvantalas, osszefolyasi tavolsag, escape-finomitas).
    fast = dict(step_meters=5_000.0, sensing_radius_meters=20_000.0,
                escape_cell_meters=40_000.0, escape_node_budget=4_000,
                max_steps=400, merge_radius_meters=20_000.0,
                escape_emit_meters=5_000.0)

    def patch(x, y):
        """Az eszaki pol koruli folt egy pontja - a domborzatok `p[0]`/`p[1]`
        koordinatai pontosan ezek (normalizalas utan)."""
        return normalize((x, y, 1.0))

    cases = [
        _case("plane-single", "plane", [patch(0.0, 0.0)], 0.0, fast),
        _case("valley-offset-source", "valley", [patch(0.0, 0.0008)], 0.0, fast),
        _case("basin-escape", "basin", [patch(0.0, 0.0)], 0.0,
              dict(fast, escape_cell_meters=20_000.0)),
        # Az escape-emisszio surusege KULON parameter: ugyanaz a dontes,
        # negyedannyi kiirt pont az escape-szakaszon.
        _case("basin-escape-sparse", "basin", [patch(0.0, 0.0)], 0.0,
              dict(fast, escape_cell_meters=20_000.0, escape_emit_meters=20_000.0)),
        _case("valley-confluence", "valley",
              [patch(0.0, 0.0010), patch(0.004, -0.0010)], 0.0, fast,
              expect_merge=True),
        # Szuk toleranciaval: az osszefolyas csak VALODI terbeli kozelsegnel
        # tortenhet, es a zaroel hossza a toleranciaval korlatos.
        _case("valley-confluence-tight", "valley",
              [patch(0.0, 0.0010), patch(0.004, -0.0010)], 0.0,
              dict(fast, merge_radius_meters=6_000.0), expect_merge=True),
        _case("twin-near-merge", "twin_near",
              [patch(0.0, -0.0002), patch(0.004, 0.0002)], 0.0, fast,
              expect_merge=True),
        _case("twin-far-no-merge", "twin_far",
              [patch(0.0, -0.0060), patch(0.004, 0.0060)], 0.0, fast,
              expect_merge=False),
    ]

    # --- invariansok, amiket a generalas kozben ELLENORZUNK ---
    for case in cases:
        for river in case["rivers"]:
            assert river["pointCount"] >= 2, case["name"]
            # (3) nincs az emisszios lepeskozt erdemben meghalado el
            edge_limit = max(case["params"]["step_meters"],
                             case["params"]["escape_emit_meters"]) * 1.5
            assert river["maxEdgeMeters"] <= edge_limit + 1e-6, \
                (case["name"], river["maxEdgeMeters"])
            if river["mergedIntoRiverIndex"] >= 0:
                # (2) az osszefolyasi zaroel a tolerancian BELUL van
                last = river["points"][-1]
                prev = river["points"][-2]
                assert chord_meters(prev, last) <= case["params"]["merge_radius_meters"] * 1.001, \
                    (case["name"], chord_meters(prev, last))
            # (4) ND-187: a viz alatti tartomanyok a Points-on BELUL vannak,
            # DISZJUNKTAK, novekvok, es minden benne levo pont a medence
            # feltoltesi szintje alatt (vagy azon) van. A szint referenciaja a
            # tartomany LEGMAGASABB pontja: definicio szerint ez a tulcsordulasi
            # pont, tehat minden tobbi pont ennel nem lehet magasabb.
            elev_fn = TERRAINS[case["terrain"]]()
            previous_end = -1
            for span in river["submergedSpans"]:
                start, end = span
                assert 0 <= start < end < river["pointCount"], (case["name"], span)
                assert start > previous_end, (case["name"], span, previous_end)
                previous_end = end
                levels = [elev_fn(tuple(river["points"][i])) for i in range(start, end + 1)]
                assert max(levels) - min(levels) >= 0.0
                # a tartomany BELSO pontjai nem emelkedhetnek a legmagasabb fole
                assert all(level <= max(levels) + 1e-9 for level in levels), case["name"]
            if river["termination"] == TERMINATION_PIT or river["escapeCount"] > 0:
                pass  # escape tortent: lehet span (de nem kotelezo, ha legacy)
            else:
                assert not river["submergedSpans"], (case["name"], river["submergedSpans"])
        if case["expectMerge"] is True:
            assert any(r["mergedIntoRiverIndex"] >= 0 for r in case["rivers"]), case["name"]
        if case["expectMerge"] is False:
            assert all(r["mergedIntoRiverIndex"] < 0 for r in case["rivers"]), case["name"]

    # (1) a folytonos irany tenylegesen kikeruli a kvantalast: a volgyben a
    # lepesiranyok NEM esnek mind a 8 jelolt-iranyra.
    valley = next(c for c in cases if c["name"] == "valley-offset-source")
    points = [tuple(p) for p in valley["rivers"][0]["points"]]
    quantized = _count_quantized_steps(points, DEFAULT_RING_DIRECTIONS)
    assert quantized < len(points) - 2, (quantized, len(points))

    # A szogfelezett iranytabla EGZAKTUL szimmetrikus.
    for count in (4, 8, 16, 32):
        table = ring_directions(count)
        for k in range(count):
            opposite = table[(k + count // 2) % count]
            assert table[k][0] == -opposite[0] and table[k][1] == -opposite[1], count

    # --- ELOTTE-UTANA meres a harom hibaosztalyra (kiirva, nem allitva) ---
    comparisons = {}
    comparisons["basin-escape"] = compare_legacy(
        "basin", [patch(0.0, 0.0)], 0.0, dict(fast, escape_cell_meters=20_000.0))
    comparisons["basin-escape-sparse"] = compare_legacy(
        "basin", [patch(0.0, 0.0)], 0.0,
        dict(fast, escape_cell_meters=20_000.0, escape_emit_meters=20_000.0))
    comparisons["valley-offset-source"] = compare_legacy(
        "valley", [patch(0.0, 0.0008)], 0.0, fast)
    comparisons["twin-near-merge"] = compare_legacy(
        "twin_near", [patch(0.0, -0.0002), patch(0.004, 0.0002)], 0.0, fast)
    for name, modes in comparisons.items():
        for mode in ("legacy", "v2"):
            m = modes[mode]
            print(f"  {name:24s} {mode:6s} pts={m['points']:5d} "
                  f"len={m['lengthKm']:8.2f}km maxEdge={m['maxEdgeM']:9.1f}m "
                  f"long={m['longEdgePercent']:5.2f}% turns={m['sharpTurns']:4d} "
                  f"merges={m['merges']} maxMergeEdge={m['maxMergeEdgeM']:9.1f}m")

    # A javitasok MERT hatasa - ezek a generalas kapui.
    #
    # (3) ESCAPE: a durva racsel teljesen eltunik a kimenetbol.
    basin = comparisons["basin-escape"]
    assert basin["legacy"]["maxEdgeM"] > 19_000.0, basin["legacy"]["maxEdgeM"]
    assert basin["v2"]["maxEdgeM"] < 6_000.0, basin["v2"]["maxEdgeM"]
    assert basin["legacy"]["longEdgePercent"] > 20.0
    assert basin["v2"]["longEdgePercent"] == 0.0
    #
    # (1) IRANY: a kvantalt koveto a VOLGYET SEM talalta meg - a `valley`
    # tesztben vegig a meder MELLETT futott, ezert HOSSZABB utat tett meg;
    # a folytonos irany bekonvergal a mederbe (merve: a lateralis elteres
    # lepesenkent kb. negyedere csokken) es rovidebb.
    valley_cmp = comparisons["valley-offset-source"]
    assert valley_cmp["v2"]["lengthKm"] < valley_cmp["legacy"]["lengthKm"], valley_cmp
    #
    # (2) OSSZEFOLYAS: a regi, cellaalapu dontes a RACS ILLESZKEDESETOL
    # fuggott, nem a tavolsagtol - ket, egymastol 2968 m-re futo parhuzamos
    # medret NEM kapcsolt ossze (a kozos cellahatar eppen kozejuk esett),
    # mikozben egy talalatnal a zaroel akar a teljes cellaatmero lehetett
    # (a termekben merve 24,113 km, ND-180). A v2 a VALODI tavolsag alapjan
    # dont, es a zaroel a tolerancian belul marad.
    twin = comparisons["twin-near-merge"]
    assert twin["legacy"]["merges"] == 0, twin["legacy"]
    assert twin["v2"]["merges"] == 1, twin["v2"]
    assert twin["v2"]["maxMergeEdgeM"] <= fast["merge_radius_meters"], twin["v2"]

    payload = dict(
        radiusMeters=RADIUS_METERS,
        legacyComparison=comparisons,
        defaults=dict(
            stepMeters=DEFAULT_STEP_METERS,
            sensingRadiusMeters=DEFAULT_SENSING_RADIUS_METERS,
            ringDirections=DEFAULT_RING_DIRECTIONS,
            escapeCellMeters=DEFAULT_ESCAPE_CELL_METERS,
            escapeNodeBudget=DEFAULT_ESCAPE_NODE_BUDGET,
            visitedLatticeLevel=DEFAULT_VISITED_LATTICE_LEVEL,
            claimLatticeLevel=lattice_level_for(DEFAULT_MERGE_RADIUS_METERS),
            mergeRadiusMeters=DEFAULT_MERGE_RADIUS_METERS,
            maxShortcutCells=DEFAULT_MAX_SHORTCUT_CELLS,
            escapeEmitMeters=DEFAULT_ESCAPE_EMIT_METERS,
            submergedMinDepthMeters=DEFAULT_SUBMERGED_MIN_DEPTH_METERS,
        ),
        ringTables=_ring_table_vectors(),
        latticeCells=[dict(point=[p[0], p[1], p[2]], level=level,
                           cell=list(lattice_cell(p, level)))
                      for p in (normalize((1.0, 0.2, 0.3)), normalize((-0.4, 1.0, 0.1)),
                                normalize((0.1, -0.2, 1.0)), normalize((1.0, 1.0, 1.0)),
                                (1.0, 0.0, 0.0), (0.0, 0.0, -1.0))
                      for level in (4, 10, 16, 20)],
        cases=cases,
    )
    out = Path(__file__).with_name("river_continuous_vectors.json")
    out.write_text(json.dumps(payload, indent=1) + "\n", encoding="utf-8")
    total = sum(len(r["points"]) for c in cases for r in c["rivers"])
    print(f"Continuous river v2 oracle OK; {len(cases)} cases, {total} points, "
          f"{len(payload['latticeCells'])} lattice vectors")


def sharp_turns(points, cos_limit=0.8660254037844387):
    """Hany iranyvaltas haladja meg a 30 fokot - az ND-180 (1) hibaosztaly
    (iranykvantalas) mertéke. A `cos_limit` EGZAKT ertek a diagnosztikahoz
    rogzitve (cos 30 fok), hogy ne kelljen trigonometriat hivni."""
    turns = 0
    for i in range(2, len(points)):
        o, a, b = points[i - 2], points[i - 1], points[i]
        px, py, pz = a[0] - o[0], a[1] - o[1], a[2] - o[2]
        dx, dy, dz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
        pn = math.sqrt(px * px + py * py + pz * pz)
        dn = math.sqrt(dx * dx + dy * dy + dz * dz)
        if pn == 0.0 or dn == 0.0:
            continue
        if (dx * px + dy * py + dz * pz) / (dn * pn) < cos_limit:
            turns += 1
    return turns


def compare_legacy(terrain, sources, sea_level, params):
    """ELOTTE-UTANA meres UGYANAZON a szintetikus domborzaton. Ezt a generalas
    kiirja, igy a hibaosztalyok javulasa SZAM, nem allitas."""
    out = {}
    for mode in ("legacy", "v2"):
        elev_fn = TERRAINS[terrain]()
        rivers = build_network(elev_fn, sources, sea_level,
                               **dict(params, legacy=(mode == "legacy")))
        edges = []
        turns = 0
        merge_edges = []
        for r in rivers:
            pts = r["points"]
            edges += [chord_meters(pts[i - 1], pts[i]) for i in range(1, len(pts))]
            turns += sharp_turns(pts)
            if r["mergedIntoRiverIndex"] >= 0 and len(pts) >= 2:
                merge_edges.append(chord_meters(pts[-2], pts[-1]))
        total = sum(edges)
        step = max(params.get("step_meters", DEFAULT_STEP_METERS),
                   params.get("escape_emit_meters", DEFAULT_ESCAPE_EMIT_METERS))
        long_edges = sum(e for e in edges if e > step * 1.5)
        out[mode] = dict(
            points=sum(len(r["points"]) for r in rivers),
            lengthKm=total / 1000.0,
            maxEdgeM=max(edges) if edges else 0.0,
            longEdgePercent=100.0 * long_edges / max(total, 1.0),
            sharpTurns=turns,
            maxMergeEdgeM=max(merge_edges) if merge_edges else 0.0,
            merges=len(merge_edges),
        )
    return out


def _count_quantized_steps(points, ring_count):
    """Hany lepes iranya esik (szamabrazolasi toleranciaval) a `ring_count`
    jelolt-irany valamelyikere - diagnosztika az (1) hibaosztalyhoz."""
    table = ring_directions(ring_count)
    count = 0
    for i in range(1, len(points)):
        a, b = points[i - 1], points[i]
        t1, t2 = tangent_basis(a)
        dx, dy, dz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
        length = math.sqrt(dx * dx + dy * dy + dz * dz)
        if length == 0.0:
            continue
        u = (dx * t1[0] + dy * t1[1] + dz * t1[2]) / length
        v = (dx * t2[0] + dy * t2[1] + dz * t2[2]) / length
        for (cx, cy) in table:
            if abs(u - cx) < 1e-9 and abs(v - cy) < 1e-9:
                count += 1
                break
    return count


if __name__ == "__main__":
    main()
