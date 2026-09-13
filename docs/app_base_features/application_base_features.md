Persze. Mivel nálatok a core szimuláció már működik, én most nem a gameplay feature-öket sorolnám, hanem azt a réteget, amitől egy Unity-projektből ténylegesen kiadható, telepíthető desktop játék/app lesz.
Az alábbi listát úgy érdemes nézni, hogy te megjelölöd majd: kell / később / nem kell, és utána ebből csinálok egy priorizált roadmapet, majd abból Claude Sonnetnek közvetlenül implementálható fejlesztési taskokat.
	Főmenü és navigáció 
		New World / New Simulation 
		Continue / Load 
		Settings 
		Credits 
		Quit 
		visszalépés a főmenübe futó szimulációból 
		megerősítő dialógus kilépéskor, ha van nem mentett állapot 
		verziószám megjelenítése 
		opcionális splash / intro képernyő 
	Világ létrehozása 
		jelenlegi bolygóparaméterek egységes New World képernyőn 
		seed kézi megadása 
		random seed 
		presetek, pl. Earth-like / Ocean World / Dry World / Extreme 
		Advanced Settings lenyitható rész 
		paraméter-validáció 
		Reset to defaults 
		Generate / Start gomb 
		esetleg rövid preview létrehozás előtt 
	Mentés és betöltés 
		Save 
		Save As 
		Load 
		autosave 
		quick save / quick load 
		save slotok 
		mentés neve 
		létrehozás dátuma 
		utolsó játékidő / simulation age 
		thumbnail a bolygóról 
		save törlése 
		save verziózás 
		régebbi mentések kompatibilitásának kezelése 
		corrupted save felismerése 
	Pause / in-game menü 
		Resume 
		Save 
		Load 
		Settings 
		Restart simulation 
		Return to Main Menu 
		Quit to Desktop 
		ESC billentyű kezelése 
	Audio rendszer 
		Master Volume 
		Music Volume 
		SFX Volume 
		UI Volume 
		Ambient Volume 
		mute 
		háttérzene 
		menüzene 
		UI kattintási hangok 
		overlay / zoom / deep-time eseményhangok 
		ambience, például szél, óceán, geológiai háttér 
		zenei loopok közti fade 
		audio mixer 
	Grafikai beállítások 
		fullscreen / borderless / windowed 
		felbontás 
		monitor választás 
		VSync 
		FPS limit 
		quality preset: Low / Medium / High / Ultra 
		anti-aliasing 
		shadow quality 
		texture quality 
		anisotropic filtering 
		volumetric effects 
		clouds 
		atmosphere quality 
		terrain / planet LOD quality 
		render scale 
		HDR opcionálisan 
		Apply / Cancel / Restore defaults 
		videobeállítások rollbackje, ha rossz felbontást választ a user 
	Gameplay / simulation settings 
		mouse sensitivity 
		zoom sensitivity 
		camera movement speed 
		inverted controls, ha releváns 
		deep-time alapértelmezett lépés 
		szimulációs speed default 
		overlay opacity 
		tooltip delay 
		confirmation dialógusok kapcsolása 
		automatikus pause bizonyos UI-k megnyitásakor 
	Input rendszer 
		keyboard + mouse 
		key binding lista 
		key rebinding 
		Restore defaults 
		input conflict detection 
		scroll / zoom kezelés 
		esetleg controller támogatás később 
	UI/UX alapfunkciók 
		egységes gomb-, panel- és popup-stílus 
		hover / pressed / disabled állapotok 
		tooltip rendszer 
		confirmation dialog rendszer 
		notification / toast rendszer 
		loading spinner / progress bar 
		modal ablak kezelés 
		back-navigation 
		UI scaling 
		különböző felbontások támogatása 
		ultrawide ellenőrzés 
	Loading rendszer 
		loading screen 
		progress indikátor 
		betöltési állapot szövegesen, pl. terrain, atmosphere, overlays 
		ne fagyjon látszólag az alkalmazás 
		generálás megszakítása, ha technikailag lehetséges 
	Első indítás 
		első indítás felismerése 
		alapbeállítások 
		opcionális Welcome képernyő 
		rövid controls/help oldal 
		grafikai preset automatikus kiválasztása hardver alapján, ha akarod 
	Help / tutorial 
		Controls oldal 
		Overlay magyarázatok 
		Deep Time magyarázat 
		Simulation Speed magyarázat 
		parameter tooltipok 
		első használatkor kontextuális tutorial 
		"Don't show again" 
	Információs UI 
		világ neve 
		seed 
		simulation age 
		aktuális idősebesség 
		pause státusz 
		FPS opcionálisan 
		aktuális overlay neve 
		fontos bolygóparaméterek gyors panelje 
	Screenshot / megosztás 
		screenshot készítés 
		UI nélküli screenshot 
		nagy felbontású screenshot opcionálisan 
		screenshot mentési könyvtár megnyitása 
	Diagnostics 
		FPS counter debug módban 
		frame time 
		GPU/CPU statok opcionálisan 
		log fájl 
		crash log 
		build verzió 
		hardware info 
		"Open log folder" 
		később bug report export 
	Hibakezelés 
		user-friendly error popup 
		exceptionök logolása 
		generálási hiba kezelése 
		save/load error kezelése 
		GPU feature hiány kezelése 
		támogatott minimum hardver ellenőrzése 
		out-of-memory helyzet kulturált kezelése, amennyire lehetséges 
	Performance 
		frame-rate target 
		háttérben futás viselkedése 
		app fókusz elvesztésekor pause / throttling 
		maximális GPU-terhelés korlátozhatósága 
		külön Simulation és Rendering quality, ami ennél a projektnél szerintem fontos 
		hosszú Deep Time ugrás közben UI responsiveness 
	Accessibility 
		UI scale 
		text scale 
		colorblind-safe overlay palette-ek 
		overlay színek testreszabása 
		nagyobb tooltip font 
		animációk csökkentése 
		villogások minimalizálása 
	Lokalizáció 
		minimum angol 
		magyar opcionálisan 
		stringek ne legyenek hardcode-olva 
		localization table már most, akkor is, ha első verzió csak angol 
	Credits / legal 
		Credits 
		engine / Unity megjelölések, ahol kell 
		third-party asset licence-ek 
		zenei licence-ek 
		font licence-ek 
		open-source licence lista 
		Privacy / telemetry szöveg, ha valaha lesz telemetry 
	Build information 
		semantic version, pl. 0.1.0 
		build number 
		build date opcionálisan 
		dev / alpha / beta / release channel 
		verzió a főmenüben és logban 
	Windows executable 
		korrekt .exe 
		alkalmazásnév 
		company / publisher mezők 
		ikon 
		executable metadata 
		megfelelő könyvtárstruktúra 
		Development Build kikapcsolva release-nél 
		architecture: x64 
		DirectX backend eldöntése 
		szükséges runtime-ok ellenőrzése 
	Telepítő 
		Setup.exe vagy MSI 
		default telepítési könyvtár 
		Start Menu shortcut 
		desktop shortcut opcionálisan 
		uninstall 
		verziószám 
		upgrade meglévő install fölé 
		save/settings ne törlődjön uninstall/update közben 
		opcionálisan portable ZIP build is 
	Update mechanizmus 
		MVP-ben szerintem nem muszáj 
		később update check 
		latest version információ 
		letöltési oldal megnyitása 
		még később auto-updater 
	Steamre való előkészítés, akkor is hasznos lehet, ha még nem Steam a cél 
		application lifecycle rendbetétele 
		save location stabil 
		fullscreen/window kezelés 
		clean quit 
		achievement rendszer helyének előkészítése 
		Steamworks integrációt viszont csak később tenném bele 
	User data elhelyezése 
		mentések 
		settings 
		screenshots 
		logs 
		cache 
		temp generált adatok 
		ezek ne a játék install könyvtárába kerüljenek 
		Unity persistentDataPath köré egységes storage service 
	Settings persistence 
		grafikai beállítások megmaradnak 
		audio beállítások megmaradnak 
		input beállítások megmaradnak 
		UI beállítások megmaradnak 
		verziózott settings fájl 
		hibás config esetén fallback defaultokra 
	Session lifecycle 
		clean startup 
		main menu 
		world creation/load 
		simulation 
		visszatérés main menübe 
		új világ indítása ugyanazon processben 
		resource cleanup 
		kilépés 
		ez Unity-projekteknél könnyen problémás, ezért külön tesztelendő 
	Release quality / QA 
		első install tiszta gépen 
		első indítás 
		új világ generálása 
		mentés 
		újraindítás 
		betöltés 
		Deep Time 
		overlayek 
		fullscreen váltás 
		felbontás váltás 
		audio 
		kilépés 
		uninstall 
		reinstall 
		régi save visszatöltése

