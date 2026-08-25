---
description: Hiba javítása költséghatékonyan, eszkalációs lépcsővel
---
Javítsd: $ARGUMENTS
1. `triage` agent (haiku): reprodukció + ha triviális és NEM determinizmus-
   érzékeny, javítás.
2. Ha "ESCALATE" vagy a hiba a random réteget/numerikus algoritmust érinti:
   - Ha a Python referencia és a C# eltér: előbb `reference-dev` ellenőrzi,
     hogy a referencia helyes-e (KAT-vektorokhoz mérve), utána `core-dev`
     javítja a C# portot a referenciához igazítva
   - Egyéb esetben `core-dev` (sonnet) javít, regressziós teszttel
3. Ha 2 kör után sem zöld: opus root-cause elemzés FÁJLBA
   (`docs/bugs/analysis-*.md`), majd a javaslatot a megfelelő agent
   implementálja.
Kész = a hibát lefedő új teszt (C# és/vagy Python) + `dotnet test` és
`verify_kat.py` zöld minden platformon.
