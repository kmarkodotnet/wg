"""
A [tilesample] naplo kiertekeloje (felhasznaloi keres, 2026-09-18).

Bemenet: egy vagy tobb `unity/WorldGenViewer/Logs/PerfLog_*.txt`. A
PlanetGridMesh render-diagnosztikaja soronkent egy mintat ir ki az
egyenletes 17x9-es kepernyo-raszterbol:

  [tilesample] frame=.. altitudeUnits=.. zoomRatio=.. viewClass=.. fov=..
               deepTimeMyr=.. col=.. row=.. sx=.. sy=.. px=.. py=..
               source=D clippedDiameterPx=.. clippedWidthPx=..
               clippedHeightPx=.. fullQuadDiameterPx=.. tile=.. face=..
               level=.. u=.. v=.. lat=.. lon=.. tileEdgeKm=.. tileEdgeUnits=..

Kimenet: zoom-savonkent egy racs, ami megmutatja, hogy ADOTT ZOOM mellett
ADOTT KEPERNYO-POZICION mekkora a tile - pixelben, a LOD-celhoz
(`targetTilePixelSize`, alapbol 8 px) viszonyitva, plusz a LOD-szint es a
fizikai (km) meret. Ez valaszolja meg kozvetlenul, hogy a kep hol es
mennyivel durvabb a celnal.

Hasznalat:
  python tools/analyze_tile_samples.py unity/WorldGenViewer/Logs/PerfLog_*.txt
  python tools/analyze_tile_samples.py --target-px 8 --source D <fajlok>

NEM produkcios kod - diagnosztikai kiertekelo.
"""
import argparse
import glob
import math
import re
import sys
from collections import defaultdict

SAMPLE_PREFIX = "[tilesample]"
KEY_VALUE = re.compile(r"(\w+)=(-?[\w.+-]+)")

# Zoom-savok a felszin feletti magassag (Unity egyseg) szerint. A LOD
# viselkedese nagysagrendekben valtozik, ezert log-lepteku savok.
ZOOM_BANDS = [
    (0.0, 0.3, "0-0.3 (tapado kozel)"),
    (0.3, 1.0, "0.3-1"),
    (1.0, 3.0, "1-3"),
    (3.0, 10.0, "3-10"),
    (10.0, 30.0, "10-30"),
    (30.0, 100.0, "30-100"),
    (100.0, float("inf"), "100+ (bolygo nezet)"),
]


def parse_samples(paths):
    samples = []
    for path in paths:
        try:
            with open(path, "r", encoding="utf-8", errors="replace") as handle:
                for line in handle:
                    index = line.find(SAMPLE_PREFIX)
                    if index < 0:
                        continue
                    fields = dict(KEY_VALUE.findall(line[index + len(SAMPLE_PREFIX):]))
                    if "clippedDiameterPx" not in fields or "col" not in fields:
                        continue
                    samples.append(fields)
        except OSError as error:
            print(f"FIGYELEM: {path} nem olvashato ({error})", file=sys.stderr)
    return samples


def to_float(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def to_int(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def band_of(altitude):
    for low, high, label in ZOOM_BANDS:
        if low <= altitude < high:
            return label
    return "?"


def median(values):
    if not values:
        return None
    ordered = sorted(values)
    middle = len(ordered) // 2
    if len(ordered) % 2 == 1:
        return ordered[middle]
    return (ordered[middle - 1] + ordered[middle]) / 2.0


def percentile(values, fraction):
    if not values:
        return None
    ordered = sorted(values)
    index = min(len(ordered) - 1, max(0, int(math.ceil(fraction * (len(ordered) - 1)))))
    return ordered[index]


def radial_zone(sx, sy):
    """Kepernyo-kozeptol mert relativ tavolsag savja - a best-first LOD
    epp a periferiat hagyja durvabban, ezert ez a legarulkodobb bontas."""
    dx, dy = sx - 0.5, sy - 0.5
    r = math.sqrt(dx * dx + dy * dy) / math.sqrt(0.5)
    if r < 0.25:
        return "kozep"
    if r < 0.55:
        return "kozepso gyuru"
    return "periferia"


def print_cut_work(samples):
    """A vagas munka-konyvelese zoom-savonkent, VAGASONKENT deduplikalva.

    Harom kerdesre valaszol (felhasznaloi keres, 2026-09-19):
      1. mennyi tile van most  -> cutLeaves / cutBudget / telitett-e,
      2. mennyi szamolodott UJRA a zoom/forgatas miatt -> newSplits, metrika,
      3. mennyi szamolas maradt abba -> ES KULON, hogy MIERT:
           * ELVONT      (starvedBudget + starvedQuota): a limit nem engedte,
                         ennyivel rosszabb a kep, mint amit a metrika kert;
           * SZUKSEGTELEN(skip*): helyesen maradt abba, a kep nem lett rosszabb.
    """
    # frame -> (sav, mezok). Egy vagas egyszer szamit.
    cuts = {}
    for s in samples:
        altitude = to_float(s.get("altitudeUnits"))
        frame = to_int(s.get("frame"))
        if altitude is None or frame is None or "cutLeaves" not in s:
            continue
        cuts[frame] = (band_of(altitude), s)
    if not cuts:
        print("\n=== Munka-konyveles ===")
        print("Nincs cutWork adat a naploban (regi formatum vagy cutWork=none).")
        return

    by_band = defaultdict(list)
    for band, fields in cuts.values():
        by_band[band].append(fields)

    print(f"\n=== Munka-konyveles ({len(cuts)} vagas, vagasonkent deduplikalva) ===")
    header = (f"{'zoom-sav':<24} {'vagas':>5} {'leaf p50':>9} {'budget':>7} {'telitett':>9} "
              f"{'ujSplit':>8} {'metrikaUj':>10} {'metrikaUjra':>12}")
    print(header)
    print("-" * len(header))
    for band_label in [label for _, _, label in ZOOM_BANDS]:
        group = by_band.get(band_label)
        if not group:
            continue
        leaves = [to_int(f.get("cutLeaves")) for f in group if to_int(f.get("cutLeaves")) is not None]
        budgets = [to_int(f.get("cutBudget")) for f in group if to_int(f.get("cutBudget")) is not None]
        saturated = sum(1 for f in group if f.get("cutSaturated") == "True")
        splits = [to_int(f.get("newSplits")) for f in group if to_int(f.get("newSplits")) is not None]
        computed = [to_int(f.get("metricComputed")) for f in group
                    if to_int(f.get("metricComputed")) is not None]
        reused = [to_int(f.get("metricReused")) for f in group
                  if to_int(f.get("metricReused")) is not None]
        print(f"{band_label:<24} {len(group):>5} "
              f"{'-' if not leaves else format(median(leaves), '.0f'):>9} "
              f"{'-' if not budgets else median(budgets):>7} "
              f"{f'{saturated}/{len(group)}':>9} "
              f"{'-' if not splits else format(median(splits), '.0f'):>8} "
              f"{'-' if not computed else format(median(computed), '.0f'):>10} "
              f"{'-' if not reused else format(median(reused), '.0f'):>12}")

    print("\n--- Abbahagyott szamitasok: MIERT (median / vagas) ---")
    header2 = (f"{'zoom-sav':<24} {'ELVONT: budget':>15} {'ELVONT: kvota':>14} "
               f"{'ok: eleg finom':>15} {'ok: maxLevel':>13} {'ok: nem lathato':>16} "
               f"{'ok: ocean':>10}")
    print(header2)
    print("-" * len(header2))
    for band_label in [label for _, _, label in ZOOM_BANDS]:
        group = by_band.get(band_label)
        if not group:
            continue

        def med(key):
            values = [to_int(f.get(key)) for f in group if to_int(f.get(key)) is not None]
            return "-" if not values else format(median(values), ".0f")

        ocean_values = [(to_int(f.get("skipOceanBase")) or 0) + (to_int(f.get("skipOceanLeaf")) or 0)
                        for f in group if "skipOceanBase" in f]
        print(f"{band_label:<24} {med('starvedBudget'):>15} {med('starvedQuota'):>14} "
              f"{med('skipSufficient'):>15} {med('skipMaxLevel'):>13} "
              f"{med('skipInvisible'):>16} "
              f"{'-' if not ocean_values else format(median(ocean_values), '.0f'):>10}")

    print("\nOLVASAS: az 'ELVONT' ket oszlopa az, ami MIATT a kep durvabb a "
          "\ncelnal - ennyi felosztast kert a metrika, es nem kapta meg. Az 'ok:'"
          "\noszlopok helyesen elhagyott munka, azok nem hibak.")


def main():
    parser = argparse.ArgumentParser(description="[tilesample] naplo kiertekelo")
    parser.add_argument("paths", nargs="+", help="PerfLog fajlok (glob is lehet)")
    parser.add_argument("--target-px", type=float, default=8.0,
                        help="A LOD celzott tile-pixelmerete (PlanetGridMesh targetTilePixelSize)")
    parser.add_argument("--source", default=None,
                        help="Csak ez a forras: S=statikus terep, D=dinamikus terep, W/w=viz")
    args = parser.parse_args()

    paths = []
    for pattern in args.paths:
        expanded = glob.glob(pattern)
        paths.extend(expanded if expanded else [pattern])

    samples = parse_samples(paths)
    if not samples:
        print("Nem talaltam [tilesample] sort. Be van kapcsolva a "
              "logUniformTileSamples es a logDrawnTileSizes az Inspectorban?")
        return 1

    if args.source:
        samples = [s for s in samples if s.get("source") == args.source]

    print(f"Beolvasva {len(samples)} minta, {len(paths)} fajlbol. "
          f"LOD-cel = {args.target_px:g} px\n")

    # 1) Zoom-sav x radialis zona: a legjobban olvashato osszefoglalo.
    grouped = defaultdict(list)
    for s in samples:
        altitude = to_float(s.get("altitudeUnits"))
        sx, sy = to_float(s.get("sx")), to_float(s.get("sy"))
        px = to_float(s.get("clippedDiameterPx"))
        if None in (altitude, sx, sy, px):
            continue
        grouped[(band_of(altitude), radial_zone(sx, sy))].append(s)

    print("=== Zoom-sav x kepernyo-zona ===")
    header = f"{'zoom-sav (magassag)':<24} {'zona':<15} {'n':>5} " \
             f"{'px p50':>8} {'px p90':>8} {'cel-arany':>10} {'szint p50':>10} {'km p50':>9}"
    print(header)
    print("-" * len(header))
    for band_label in [label for _, _, label in ZOOM_BANDS]:
        for zone in ("kozep", "kozepso gyuru", "periferia"):
            group = grouped.get((band_label, zone))
            if not group:
                continue
            pixels = [to_float(s["clippedDiameterPx"]) for s in group
                      if to_float(s.get("clippedDiameterPx")) is not None]
            levels = [to_int(s.get("level")) for s in group if to_int(s.get("level")) is not None]
            kms = [to_float(s.get("tileEdgeKm")) for s in group
                   if to_float(s.get("tileEdgeKm")) is not None]
            p50, p90 = median(pixels), percentile(pixels, 0.9)
            ratio = p50 / args.target_px if p50 is not None and args.target_px > 0 else None
            print(f"{band_label:<24} {zone:<15} {len(group):>5} "
                  f"{p50 if p50 is None else round(p50, 2):>8} "
                  f"{p90 if p90 is None else round(p90, 2):>8} "
                  f"{'-' if ratio is None else format(ratio, '.2f') + 'x':>10} "
                  f"{median(levels) if levels else '-':>10} "
                  f"{'-' if not kms else format(median(kms), '.2f'):>9}")

    # 1b) Munka-konyveles (felhasznaloi keres, 2026-09-19): mennyi tile van,
    # mennyi szamolodott ujra, mennyi maradt abba - es MIERT maradt abba.
    #
    # A szamlalok VAGASONKENT azonosak (egy meres 153 mintaja ugyanazt a
    # vagast irja ki), ezert `frame` szerint DEDUPLIKALUNK. Minta-szam szerint
    # sulyozva a sok mintat ado zoom-savok felulreprezentaltak lennenek.
    print_cut_work(samples)

    # 2) Racs-terkep zoom-savonkent: hol durvul el a kep a kepernyon.
    print("\n=== Racs-terkep: median tile-pixelmeret kepernyo-pozicio szerint ===")
    print("(soronkent felulrol lefele, oszloponkent balrol jobbra; '.' = nincs minta)")
    by_band_cell = defaultdict(lambda: defaultdict(list))
    max_col = max_row = 0
    for s in samples:
        altitude = to_float(s.get("altitudeUnits"))
        col, row = to_int(s.get("col")), to_int(s.get("row"))
        px = to_float(s.get("clippedDiameterPx"))
        if None in (altitude, col, row, px):
            continue
        by_band_cell[band_of(altitude)][(col, row)].append(px)
        max_col, max_row = max(max_col, col), max(max_row, row)

    for band_label in [label for _, _, label in ZOOM_BANDS]:
        cells = by_band_cell.get(band_label)
        if not cells:
            continue
        counts = sum(len(v) for v in cells.values())
        print(f"\n-- zoom-sav: {band_label}  (minta: {counts}) --")
        for row in range(max_row, -1, -1):
            parts = []
            for col in range(max_col + 1):
                values = cells.get((col, row))
                parts.append("    ." if not values else f"{median(values):5.1f}")
            print(" ".join(parts))

    # 3) Fizikai ellenorzes: a szint es a km-es meret osszefugg-e.
    print("\n=== LOD-szint -> tenyleges fizikai tile-elhossz (km) ===")
    by_level = defaultdict(list)
    for s in samples:
        level, km = to_int(s.get("level")), to_float(s.get("tileEdgeKm"))
        if level is None or km is None or level < 0:
            continue
        by_level[level].append(km)
    for level in sorted(by_level):
        kms = by_level[level]
        print(f"  level {level:>2}: n={len(kms):>5} km p50={median(kms):8.3f} "
              f"min={min(kms):8.3f} max={max(kms):8.3f}")

    return 0


def _self_test():
    """Parser-onellenorzes valodi naplo nelkul."""
    line = ("[tilesample] frame=42 altitudeUnits=2.500000 zoomRatio=40.000 viewClass=Region "
            "fov=60.000 deepTimeMyr=0 "
            "cutLeaves=8000 dynLeaves=7900 waterLeaves=120 cutBudget=8000 cutSaturated=True "
            "newSplits=1024 reusedLeaves=6876 metricComputed=203 metricReused=98999 "
            "starvedBudget=7201 starvedQuota=13095 skipSufficient=0 skipMaxLevel=4 "
            "skipInvisible=1013 skipOceanBase=142 skipOceanLeaf=0 "
            "col=8 row=4 sx=0.500 sy=0.500 px=598.000 py=355.000 "
            "source=D clippedDiameterPx=19.250 clippedWidthPx=18.000 clippedHeightPx=20.000 "
            "fullQuadDiameterPx=19.900 tile=1C00000000000ABC face=3 level=12 u=1234 v=567 "
            "lat=41.230 lon=-17.840 tileEdgeKm=1.905 tileEdgeUnits=0.029900")
    fields = dict(KEY_VALUE.findall(line[line.find(SAMPLE_PREFIX) + len(SAMPLE_PREFIX):]))
    assert to_int(fields["col"]) == 8 and to_int(fields["row"]) == 4, fields
    assert abs(to_float(fields["clippedDiameterPx"]) - 19.25) < 1e-9
    assert to_int(fields["level"]) == 12
    assert abs(to_float(fields["lat"]) - 41.23) < 1e-9
    assert abs(to_float(fields["lon"]) + 17.84) < 1e-9
    assert abs(to_float(fields["tileEdgeKm"]) - 1.905) < 1e-9
    # A munka-konyveles mezoi (2026-09-19): az ELVONT es a SZUKSEGTELEN
    # megallas KULON olvasodik ki - ezek osszekeveresebol szuletett a
    # 2026-09-18-i teves jelentesem.
    assert to_int(fields["cutLeaves"]) == 8000 and to_int(fields["cutBudget"]) == 8000
    assert fields["cutSaturated"] == "True"
    assert to_int(fields["newSplits"]) == 1024
    assert to_int(fields["starvedBudget"]) == 7201, fields["starvedBudget"]
    assert to_int(fields["starvedQuota"]) == 13095
    assert to_int(fields["skipInvisible"]) == 1013
    assert band_of(2.5) == "1-3", band_of(2.5)
    assert radial_zone(0.5, 0.5) == "kozep"
    assert radial_zone(0.95, 0.95) == "periferia"
    assert median([1, 2, 3, 4]) == 2.5
    print("OK - parser es a savok onellenorzese rendben")


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--self-test":
        _self_test()
    else:
        sys.exit(main())
