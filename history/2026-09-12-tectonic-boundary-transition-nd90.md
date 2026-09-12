# Folytonos tektonikai kéregperem — ND-90 (2026-09-12)

## Kiinduló hiba

A precíz kilométer-lépték egyes lemezütközéseknél kb. 200 km magasnak látszó,
közvetlen falat mutatott. Az ND-88 feltárta, hogy ebből 111,3× megjelenítési
túlrajzolás származott, de a Core-ban is maradt valódi diszkontinuitás: a
legközelebbi lemez váltásakor az óceáni és kontinentális kéregbázis -4000 m és
+800 m között egy pontban ugorhatott. A deep-time erózió csak az ettől különálló
upliftet relaxálta.

## Megoldás

- Eltérő kéregtípusú két legközelebbi lemez között `0.005` gap-sávban a Core
  ugyanazon pozíció két báziselevációját keveri.
- A második lemez súlya a határon 0,5, a sáv külső szélén 0; a köztes súly
  polinomiális smoothstep, új transzcendens művelet nélkül.
- A sávon kívüli és azonos kéregtípusú út változatlan. Az `isOceanic` továbbra
  is a legközelebbi lemez diszkrét anyagtulajdonsága.
- A statikus, `TerrainPointBasis` és deep-time kiértékelés közös képletet kapott.
- A tektonikus uplift külön felső korlátja 1500 m-ről 1000 m-re csökkent.
  Ez nem a teljes felszíni eleváció clampje: a kéregbázis és a domborzati zaj
  ennél magasabb hegyet továbbra is létrehozhat.
- A numerikus világkép-változás miatt a `.worldpkg` formátum 2-es; az 1-es
  csomag explicit inkompatibilitási hibát ad.

## Kalibráció és következmény

A `0.005` sávot a 0,01/0,005/0,0025 jelöltek közül választottuk. A kanonikus
level-6, rögzített víztérfogatú próba 250 Myr-nél 95,036% vízborítást ad. Ez a
régi 95%-os durva összeomlásőr fölött 0,036 százalékpont; ezért az őr felső
határa dokumentáltan 96%-ra változott. Ez nem új cél-vízarány.

## Ellenőrzés

- A teljes Python downstream-lánc újragenerálva. A level-5 lemezhatár-minta
  1010/6144 pontja (16,4%) kapott upliftet, maximum 726,6 m-rel; a külön
  szintetikus regresszió az egzakt 1000 m-es plafont is eléri.
- A deep-time referencia `t=0` egyezése 288 mintán legfeljebb
  `4,55e-13 m` eltérésű; minden további determinisztikus ellenőrzés zöld.
- A kanonikus level-6 világ: 65,0% cél-vízarány, 31 kontinens, 421 legalább
  öt tile-os régió. Az új state hash:
  `dd685aac9df276053fcb6bb58cffa38100a725961065d0a7688b1aff19472f12`.
- A 12 finom folyóút mind hurokmentes és nettó lejtésű; 11 óceánba, 1
  természetes mélyedésbe fut.
- Threefry KAT 9/9. Az 512 elemű alapvektor és mind a kilenc érintett
  downstream referencia-/tesztadat-pár SHA-256 alapján bájtra azonos.
- `dotnet test WorldGen.sln -c Release --no-build --no-restore`:
  Core 384/384, CLI 8/8, viewer-LOD 290/290 PASS.
- `dotnet build WorldGen.sln -c Release --no-restore`: 0 warning, 0 error.
- Az offline, Unity által generált `Assembly-CSharp.csproj` nem tiszta kapu:
  a meglévő nullable/obsolete hibák mellett a párhuzamos kamera-partial
  frissítés miatt stale projektből hiányzó szimbólumokat is jelez. Az ND-88/90
  új soraira nem adott hibát, de ez nem helyettesíti az Editor-kompilációt.

Élő Unity-vizuális elfogadás szükséges ugyanazon problémás peremnél és több
deep-time időpontban; parancssori teszt ezt nem helyettesíti.
