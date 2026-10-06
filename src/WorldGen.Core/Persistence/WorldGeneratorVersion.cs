namespace WorldGen.Core.Persistence
{
    /// <summary>
    /// ND-108: a teljes numerikus világmodell kompatibilitási azonosítója.
    /// Minden seed-/világadat-törő módosításkor emelendő, az érintett ND-ben
    /// dokumentálva. Nem alkalmazás- vagy fájlformátum-verzió.
    /// </summary>
    public static class WorldGeneratorVersion
    {
        // ND-197 (b) + ND-198 (SEED-TÖRŐ, 2026-10-06): (1) a folyó-forrás KERETE a
        // forrásképes (nedves, hegyvidéki) tile-ok SZÁMÁBÓL jön, és MINDEN
        // méretküszöb fölötti vízgyűjtő versenyez érte — a „16 legnagyobb"
        // vágás megszűnt, a méret- és magasság-küszöb 12 → 2 tile, 300 → 150 m.
        // MÉRVE: a nedves szárazföld 51,2%-a volt folyó nélkül, most 19,5%.
        // (2) A TAVAK VÍZMÉRLEGET kaptak: a szint ott áll be, ahol a vízgyűjtő
        // beáramlása fedezi a tófelszín párolgását — a csapadékmentes medencék
        // tavai eltűnnek, a többi a mérleg szerinti szintre áll.
        // Előzmény — ND-196 (b) (SEED-TÖRŐ, 2026-10-06): a folyó-forrás KVÓTÁJA a medencék
        // CSAPADÉK-ÖSSZEGÉVEL arányos, abszolút alsó kapuval — nem fix 6/medence.
        // MÉRVE (seed 0xA7C944210000, level 5, a hőmodell párolgásával): a régi
        // kvótával 48/96 forrás (50%) PONTOSAN nulla csapadékú tile-ról indult,
        // 7 medence mind a 6 forrása, és a folyó-nyomvonal 55,6%-a nulla
        // csapadékú szárazföldön futott. A forráslista, és így MINDEN
        // folyóhálózat új.
        // Előzmény — ND-189 (SEED-TÖRŐ, 2026-10-03): a folyó-FORRÁS nem indulhat olyan
        // pontból, ami a nyomkövető FINOM mezőjén a tengerszint alatt van,
        // és nem eshet a durva mezőn látható TÓ alá. MÉRVE a t=0 hálózaton:
        // 96 forrásból 17 tó alatt, 2 a tengerszint alatt volt (ez a kettő
        // adta a 0,00 km hosszú "folyókat"). A forráslista, és így MINDEN
        // folyóhálózat új. (A víz alatti szakaszok jelölése — ND-187 — ettől
        // független és NEM seed-törő: származtatott adat a változatlan
        // nyomvonal-geometriából.)
        // Előzmény — ND-186 (az ND-180 "C" opciója, 1. kör): a FOLYTONOS folyó-nyomkövető
        // három mért modellhibája javítva, mindhárom numerikus:
        //   (1) a lépésirány már nem a 8 jelölt-irány egyikére kvantált, hanem
        //       a jelölt-kör első harmonikusából számolt folytonos lejtésirány;
        //   (2) az összefolyás VALÓDI térbeli közelségvizsgálat (<= 100 m),
        //       nem "ugyanabban a 13-18 km-es finom tile-ban" - a mért
        //       24,113 km-es összefolyási teleport ezzel strukturálisan kizárt;
        //   (3) az escape-szakasz "víz alatti" egyenesekre van összevonva és a
        //       lépésközre mintavételezve, tehát a 2,0 / 2,828 km-es rácsélek
        //       eltűnnek a nyomvonalból.
        // Ugyanitt szűnt meg két csendes I1-sértés a folyó kritikus útján: a
        // jelölt-irányok `Math.Cos`/`Math.Sin`-je (ND-23) és a hurok-védelem
        // `TileGeometry.FromPosition`-je, ami `Math.Atan`-t hív (ND-24).
        // MINDEN folyóhálózat (nyomvonal, összefolyás-fa, vízhozam-súly) új.
        // Előzmény — ND-168/169: az éves bázis meridionális szállítása energiamegmaradó
        // élfluxus, a végleges B menet éves szele a csapadékot is táplálja.
        // Előzmény — ND-160: a hőmodell BÁZISÁNAK radiatív tagja BOLYGÓ-albedóval számol
        // (a felszíni albedó a tickenkénti anomália-tagban marad). A bázis és a
        // belőle differenciált termikus szél is változik, tehát minden
        // hőmodell-kimenet (éves éghajlat, jégosztály, biome, csapadék) új.
        // Előzmény — ND-137 2. kör: folyóvízi bevágódás (a relief előbb NŐ, majd lekopik)
        // + parti abrázió (a tengerszint körüli sáv a tengerszint felé
        // planálódik). Csak a `t > 0` deep-time kimenet változik; a statikus
        // (t = 0) világok bitre azonosak maradnak.
        // Előzmény — ND-137 (A20): valódi deep-time erózió — a relief-zajtagok
        // hullámhossz-szelektív, hidrológia-vezérelt csillapítása, és a modul
        // átállítása a DeterministicMath Exp/Sin/Asin-jára (korábban nyers
        // System.Math, tehát platformok között nem garantált).
        // Előzmény — ND-136 (A19): a domborzati zaj a lemez saját
        // vonatkoztatási rendszerében értékelődik ki, tehát együtt vándorol
        // a kéreggel.
        // Előzmény — ND-142: a pillanatnyi levegőanomália visszahat a termikus szélre.
        // ND-165/174/175: analitikus bolygó-albedó, periodikus szezonális energiamérleg
        // és fizikai hó/jégbesorolás.
        public const string Current = "12";
    }
}
