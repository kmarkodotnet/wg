#!/usr/bin/env python3
"""
ND-20 kapu: Burst FloatMode.Strict kikenyszeritese (A13).

A PROBLEMA. A Burst compiler alapbol FloatMode.Default modban fordit, ami
engedelyezi a lebegopontos muveletek atrendezeset (fast-math). Ez csendben
megserti az I1 invariansot: ugyanaz a seed mas platformon / mas Burst-verzion
mas bitkepet ad. Egyetlen hianyzo attributum-parameter csak a platformok
kozotti hash-elteresnel derul ki, ami nagyon draga hibakereses.

A SZABALY (CLAUDE.md, docs/04-decisions.md ND-20). Minden [BurstCompile]
attributum-hasznalatnak explicit FloatMode = FloatMode.Strict parametert kell
kapnia. Ezen felul a FloatPrecision nem lehet Low vagy Medium (azok explicit
approximaciot engedelyeznek); a Standard es a High rendben van.

MIERT MOST, MIKOR NULLA A TALALAT. A repoban ma egyetlen [BurstCompile] sincs
(az ND-39 "A" opcioja meg nem indult el). A kapu szandekosan ELORE keszult el,
az elso Burst-hasznalat ELOTT: igy a kapu a bevezeto commit-tal EGYUTT valt
pirosra, nem utolag, foltozaskent. Az ures halmazon a kapu zold - es hogy ez ne
"vakon zold" legyen, a --self-test a sajat pozitiv es negativ fixture-keszleten
bizonyitja, hogy a kapu valoban fog.

HASZNALAT
    python tools/ci/check_burst_strict.py               # a repo atvizsgalasa
    python tools/ci/check_burst_strict.py --self-test   # a kapu onellenorzese
    python tools/ci/check_burst_strict.py --verbose     # a talalatok kilistazasa

KILEPESI KOD
    0 = nincs sertes;  1 = sertes (vagy elhasalt self-test);  2 = hasznalati hiba
"""

import argparse
import os
import re
import sys

# A repo gyokere ehhez a fajlhoz kepest: tools/ci/ -> ../..
REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))

# Amit atvizsgalunk: minden C# forras, ami a mi kodunk - a motorfuggetlen mag,
# a tesztek, az eszkozok es a Unity-oldali Assets.
SCAN_ROOTS = [
    "src",
    "tests",
    "tools",
    os.path.join("unity", "WorldGenViewer", "Assets"),
]

# Generalt / kulso konyvtarak: itt nem a mi kodunk van, es a Library/PackageCache
# alatt maga a Burst package is elofordul (tele sajat [BurstCompile]-lal).
EXCLUDED_DIRS = {
    ".git",
    "bin",
    "obj",
    "Library",
    "PackageCache",
    "Temp",
    "Logs",
    "UserSettings",
    "artifacts",
    "node_modules",
}

# A BurstCompile attributum-token. Megengedjuk a nevter-kvalifikaciot
# (Unity.Burst.BurstCompile) es az "Attribute" utotagot is, mert C#-ban
# mindketto ervenyes attributum-iras.
_ATTR_TOKEN = re.compile(
    r"(?<![A-Za-z0-9_.])(?:(?:global::)?Unity\.Burst\.)?BurstCompile(?:Attribute)?(?![A-Za-z0-9_])"
)

_FLOAT_MODE_STRICT = re.compile(
    r"\bFloatMode\s*=\s*(?:(?:global::)?Unity\.Burst\.)?FloatMode\s*\.\s*Strict\b"
)
_FLOAT_MODE_ANY = re.compile(r"\bFloatMode\s*=\s*[^,)]+")
_FLOAT_PRECISION_BAD = re.compile(
    r"\bFloatPrecision\s*=\s*(?:(?:global::)?Unity\.Burst\.)?FloatPrecision\s*\.\s*(Low|Medium)\b"
)

_DOUBLE_QUOTE = chr(34)
_SINGLE_QUOTE = chr(39)


def strip_comments_and_strings(text):
    """Kommenteket es sztringeket szokozre cserel, a hosszt es a sortoreseket megtartva.

    Igy a sor- es karakterpoziciok valtozatlanok (a hibauzenet sorszamot ir), de
    egy kommentben vagy sztringben emlitett "BurstCompile" nem lesz talalat.
    """
    out = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        # // sorkomment
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                out.append(" ")
                i += 1
            continue
        # /* blokk-komment */
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            out.append("  ")
            i += 2
            while i < n and not (text[i] == "*" and i + 1 < n and text[i + 1] == "/"):
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            if i < n:
                out.append("  ")
                i += 2
            continue
        # verbatim sztring: @"..." (a "" az escape-elt idezojel)
        if c == "@" and i + 1 < n and text[i + 1] == _DOUBLE_QUOTE:
            out.append("  ")
            i += 2
            while i < n:
                if text[i] == _DOUBLE_QUOTE:
                    if i + 1 < n and text[i + 1] == _DOUBLE_QUOTE:
                        out.append("  ")
                        i += 2
                        continue
                    out.append(" ")
                    i += 1
                    break
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            continue
        # sima sztring vagy karakter-literal
        if c == _DOUBLE_QUOTE or c == _SINGLE_QUOTE:
            quote = c
            out.append(" ")
            i += 1
            while i < n:
                if text[i] == "\\" and i + 1 < n:
                    out.append("  ")
                    i += 2
                    continue
                if text[i] == quote:
                    out.append(" ")
                    i += 1
                    break
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def _read_balanced_parens(text, start):
    """A start indexen nyilo '(' parjaig olvas. (arg_szoveg, veg_index) vagy (None, start)."""
    if start >= len(text) or text[start] != "(":
        return None, start
    depth = 0
    i = start
    while i < len(text):
        if text[i] == "(":
            depth += 1
        elif text[i] == ")":
            depth -= 1
            if depth == 0:
                return text[start + 1:i], i + 1
        i += 1
    return None, start  # lezaratlan - szintaktikailag rossz forras


def _is_attribute_usage(code, token_start):
    """Igaz, ha a token attributum-listaban van: visszafele '[' -ig csak vesszo /
    whitespace / attributum-cel (assembly:, module:, method: ...) allhat.

    Ez szuri ki a typeof(BurstCompileAttribute)-szeru, nem attributum-hasznalatu
    emliteseket."""
    i = token_start - 1
    while i >= 0:
        c = code[i]
        if c in " \t\r\n,:":
            i -= 1
            continue
        if c == "[":
            return True
        if c == ")":
            # Egy MEGELOZO attributum parameterlistaja ugyanabban a listaban,
            # pl. [StructLayout(LayoutKind.Sequential), BurstCompile] -> a
            # kiegyensulyozott zarojel-part atlepjuk.
            depth = 0
            while i >= 0:
                if code[i] == ")":
                    depth += 1
                elif code[i] == "(":
                    depth -= 1
                    if depth == 0:
                        break
                i -= 1
            if i < 0:
                return False
            i -= 1
            continue
        if c.isalnum() or c == "_" or c == ".":
            # attributum-cel vagy megelozo attributum neve - tovabb visszafele
            i -= 1
            continue
        return False
    return False


def _attribute_hits(code):
    return [m for m in _ATTR_TOKEN.finditer(code) if _is_attribute_usage(code, m.start())]


def check_source(text, path="<memory>"):
    """Egy C# forras atvizsgalasa. A talalt sertesek listajat adja vissza."""
    code = strip_comments_and_strings(text)
    violations = []
    for match in _attribute_hits(code):
        line = code.count("\n", 0, match.start()) + 1
        j = match.end()
        while j < len(code) and code[j] in " \t\r\n":
            j += 1
        args, _ = _read_balanced_parens(code, j)
        if args is None:
            violations.append(
                (path, line, "[BurstCompile] parameter nelkul - FloatMode = FloatMode.Strict kotelezo (ND-20)")
            )
            continue
        if not _FLOAT_MODE_STRICT.search(args):
            found = _FLOAT_MODE_ANY.search(args)
            detail = found.group(0).strip() if found else "nincs FloatMode parameter"
            violations.append(
                (path, line, "[BurstCompile] FloatMode = FloatMode.Strict nelkul ({0}) - ND-20".format(detail))
            )
        bad_precision = _FLOAT_PRECISION_BAD.search(args)
        if bad_precision:
            violations.append(
                (
                    path,
                    line,
                    "[BurstCompile] FloatPrecision.{0} approximaciot engedelyez - csak Standard vagy High (ND-20)".format(
                        bad_precision.group(1)
                    ),
                )
            )
    return violations


def iter_cs_files(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in EXCLUDED_DIRS]
        for name in filenames:
            if name.endswith(".cs"):
                yield os.path.join(dirpath, name)


def scan_repo(repo_root, verbose=False):
    violations = []
    scanned = 0
    attributes_found = 0
    for rel in SCAN_ROOTS:
        root = os.path.join(repo_root, rel)
        if not os.path.isdir(root):
            continue
        for path in iter_cs_files(root):
            scanned += 1
            with open(path, "r", encoding="utf-8-sig", errors="replace") as handle:
                text = handle.read()
            if "BurstCompile" not in text:
                continue
            display = os.path.relpath(path, repo_root).replace("\\", "/")
            code = strip_comments_and_strings(text)
            hits = _attribute_hits(code)
            attributes_found += len(hits)
            if verbose and hits:
                for hit in hits:
                    print("  [BurstCompile] {0}:{1}".format(display, code.count("\n", 0, hit.start()) + 1))
            violations.extend(check_source(text, display))
    return scanned, attributes_found, violations


# --------------------------------------------------------------------------
# Self-test: a kapu bizonyitasa nem-ures halmazon.
#
# Ez a resz azert letezik, mert a repoban ma nulla [BurstCompile] van: egy
# mindig-zold kapu ertektelen. A fixture-ok pontosan azokat az iras-modokat
# jarjak vegig, amikkel egy fejleszto realisan talalkozik.
# --------------------------------------------------------------------------

_PASS_FIXTURES = [
    ("egyszeru strict", "[BurstCompile(FloatMode = FloatMode.Strict)]\nstruct J {}\n"),
    ("szokoz nelkul", "[BurstCompile(FloatMode=FloatMode.Strict)]\nstruct J {}\n"),
    (
        "kvalifikalt nevter",
        "[Unity.Burst.BurstCompile(FloatMode = Unity.Burst.FloatMode.Strict)]\nstruct J {}\n",
    ),
    ("Attribute utotag", "[BurstCompileAttribute(FloatMode = FloatMode.Strict)]\nstruct J {}\n"),
    (
        "tobb parameter, sortoressel",
        "[BurstCompile(\n    CompileSynchronously = true,\n    FloatMode = FloatMode.Strict,\n"
        "    FloatPrecision = FloatPrecision.High)]\nstruct J {}\n",
    ),
    (
        "osszevont attributum-lista",
        "[StructLayout(LayoutKind.Sequential), BurstCompile(FloatMode = FloatMode.Strict)]\nstruct J {}\n",
    ),
    ("assembly-szintu", "[assembly: BurstCompile(FloatMode = FloatMode.Strict)]\n"),
    (
        "standard precision megengedett",
        "[BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]\nstruct J {}\n",
    ),
    ("kommentben emlitve - nem attributum", "// TODO: [BurstCompile] majd ide is kell\nstruct J {}\n"),
    ("blokk-kommentben emlitve", "/* [BurstCompile]\n   tobb soron at */\nstruct J {}\n"),
    ("sztringben emlitve", "class C { const string S = " + _DOUBLE_QUOTE + "[BurstCompile]" + _DOUBLE_QUOTE + "; }\n"),
    (
        "verbatim sztringben emlitve",
        "class C { const string S = @" + _DOUBLE_QUOTE + "[BurstCompile(FloatMode = FloatMode.Fast)]" + _DOUBLE_QUOTE + "; }\n",
    ),
    ("typeof-hivatkozas - nem attributum-hasznalat", "class C { Type T = typeof(BurstCompileAttribute); }\n"),
]

_FAIL_FIXTURES = [
    ("csupasz attributum", "[BurstCompile]\nstruct J {}\n"),
    ("ures parameterlista", "[BurstCompile()]\nstruct J {}\n"),
    ("FloatMode.Fast", "[BurstCompile(FloatMode = FloatMode.Fast)]\nstruct J {}\n"),
    ("FloatMode.Default", "[BurstCompile(FloatMode = FloatMode.Default)]\nstruct J {}\n"),
    ("FloatMode.Deterministic", "[BurstCompile(FloatMode = FloatMode.Deterministic)]\nstruct J {}\n"),
    ("csak CompileSynchronously", "[BurstCompile(CompileSynchronously = true)]\nstruct J {}\n"),
    ("csak FloatPrecision", "[BurstCompile(FloatPrecision = FloatPrecision.High)]\nstruct J {}\n"),
    (
        "strict, de FloatPrecision.Low",
        "[BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Low)]\nstruct J {}\n",
    ),
    (
        "strict, de FloatPrecision.Medium",
        "[BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Medium)]\nstruct J {}\n",
    ),
    ("assembly-szintu, strict nelkul", "[assembly: BurstCompile]\n"),
    ("kvalifikalt, strict nelkul", "[Unity.Burst.BurstCompile(FloatMode = FloatMode.Fast)]\nstruct J {}\n"),
    (
        "osszevont listaban, strict nelkul",
        "[StructLayout(LayoutKind.Sequential), BurstCompile]\nstruct J {}\n",
    ),
    (
        "ket attributum, a masodik hibas",
        "[BurstCompile(FloatMode = FloatMode.Strict)]\nstruct A {}\n\n[BurstCompile]\nstruct B {}\n",
    ),
    (
        "komment utan igazi sertes ugyanabban a fajlban",
        "// [BurstCompile] emlites\n[BurstCompile(FloatMode = FloatMode.Fast)]\nstruct J {}\n",
    ),
]


def run_self_test():
    failures = []
    for name, source in _PASS_FIXTURES:
        found = check_source(source, "<pass:{0}>".format(name))
        if found:
            failures.append("ATENGEDENDO fixture elhasalt: {0} -> {1}".format(name, found))
    for name, source in _FAIL_FIXTURES:
        found = check_source(source, "<fail:{0}>".format(name))
        if not found:
            failures.append("ELUTASITANDO fixture atment: {0}".format(name))

    # A sorszamnak hasznalhatonak kell lennie: a komment-strippeles nem tolhatja el.
    multi = "// fejlec\n\n[BurstCompile]\nstruct J {}\n"
    found = check_source(multi, "<line>")
    if not found or found[0][1] != 3:
        failures.append("sorszam-elteres: {0} (elvart sor: 3)".format(found))

    total = len(_PASS_FIXTURES) + len(_FAIL_FIXTURES) + 1
    if failures:
        print("Burst-kapu self-test: ELHASALT ({0} hiba / {1} eset)".format(len(failures), total))
        for line in failures:
            print("  - " + line)
        return 1
    print(
        "Burst-kapu self-test: OK ({0} eset - {1} atengedendo, {2} elutasitando, 1 sorszam)".format(
            total, len(_PASS_FIXTURES), len(_FAIL_FIXTURES)
        )
    )
    return 0


def main(argv):
    parser = argparse.ArgumentParser(
        description="ND-20 kapu: minden [BurstCompile] FloatMode = FloatMode.Strict-et kap-e"
    )
    parser.add_argument("--root", default=REPO_ROOT, help="a repo gyokere (alapbol a szkript helyebol szamolva)")
    parser.add_argument("--self-test", action="store_true", help="a kapu onellenorzese fixture-okon")
    parser.add_argument("--verbose", action="store_true", help="a megtalalt [BurstCompile] helyek kilistazasa")
    args = parser.parse_args(argv)

    if args.self_test:
        return run_self_test()

    root = os.path.abspath(args.root)
    if not os.path.isdir(root):
        print("HIBA: a megadott gyoker nem konyvtar: {0}".format(root))
        return 2

    scanned, attributes_found, violations = scan_repo(root, verbose=args.verbose)

    if violations:
        print("ND-20 SERTES: {0} hibas [BurstCompile] attributum".format(len(violations)))
        for path, line, message in violations:
            print("  {0}:{1}: {2}".format(path, line, message))
        print("")
        print("A Burst alapbol FloatMode.Default-tal fordit, ami atrendezheti a")
        print("lebegopontos muveleteket -> I1 (determinizmus) sertes. Javitas:")
        print("  [BurstCompile(FloatMode = FloatMode.Strict)]")
        return 1

    if attributes_found == 0:
        print(
            "ND-20 kapu: OK - {0} C# fajl atvizsgalva, egyetlen [BurstCompile] sincs "
            "(a kapu elore keszult, ld. ND-20).".format(scanned)
        )
    else:
        print(
            "ND-20 kapu: OK - {0} C# fajl, {1} [BurstCompile], mind FloatMode.Strict.".format(
                scanned, attributes_found
            )
        )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
