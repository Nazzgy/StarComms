# Zadání ke spuštění v Codexu

Implementuj kompletní Windows aplikaci StarComms podle `STARCOMMS_SPEC.md` a
vizuálních podkladů v `assets/`. Nejdřív si přečti `AGENTS.md` a celou specifikaci.

Pracuj samostatně až do funkčního celku: skutečný mikrofon a přehrávání, síťový
host uvnitř aplikace, připojování hráčů, lodě, oprávnění, oddělený kapitánský hovor,
globální push-to-talk během hry, obnova spojení a srozumitelné české rozhraní.

Výstup pro hráče musí být **jediný samostatný `StarComms.exe` pro Windows x64**.
Bez instalátoru, bez sousedních DLL a konfiguračních souborů nutných ke spuštění,
bez instalace .NET, bez dalšího serverového programu a bez povinné internetové
služby. EXE si uživatel sám zabalí a pošle ostatním.

Logování je ve výchozím stavu vypnuté. Přepínač `Logs on` v nastavení zapne
diagnostické logy přímo vedle skutečného `StarComms.exe`; žádné tiché ukládání jinam.
Na všech obrazovkách je v pravém dolním rohu viditelná verze i číslo sestavení.
Při připojení upozorni na odlišné sestavení hostitele.

Dodrž rozhodnutý stack a protokol. Drobné nejasnosti vyřeš rozumně sám a zapiš
rozhodnutí. Nezastavuj se po vytvoření plánu, návrhu UI nebo prvního prototypu.
Jednotlivé implementační etapy jsou pracovní pořadí, nikoli důvod končit úkol.

Proveď build, automatické testy, simulaci dvanácti klientů a publikování EXE.
Pokud máš dostupné Windows, spusť skutečnou aplikaci a ověř její běh. Testy
vyžadující dvě domácí sítě nebo Star Citizen označ poctivě podle toho, zda jsi je
skutečně provedl. Chybějící prostředí nepředstírej; dokonči ostatní práci.

Na konci odevzdej zdrojový projekt, výsledný EXE, stručný český návod a přehled
splněných akceptačních testů. Instalační balíček nevytvářej.
