# Rozhodnutí při implementaci

- Název aplikace, EXE, schématu pozvánky i profilu je StarComms. Identifikátor FCV1 v síťové obálce zachovává kompatibilitu se zadáním.
- Host rozhoduje o oprávnění každého rámce pod stejným zámkem jako při změně role. Jeho vlastní hlasový klient používá stejný šifrovaný přenos jako ostatní.
- Schannel na testovacím Windows vyžadoval uživatelský kontext soukromého klíče. Pro trvalou identitu aplikace chrání exportovaný klíč pomocí DPAPI aktuálního uživatele.
- Při uvolnění PTT se zahodí čekající nezakódované vzorky. Kodér pracuje mimo zámek audio callbacku a před odesláním znovu ověří aktivní stisk.
- Windows testy otevírají vlastní kopie EXE s explicitními dočasnými profily pomocí přepínače `--test-profile`. Běžné spuštění používá LocalAppData. Místo diagnostických logů se odvozuje od cesty procesu i při testování.
- Publikace používá výsledek předchozího uzamčeného restore. Zabrání se tak přepsání zámků knihoven odlišným RID při druhém restore během publish.
- Hlasová aktivace posádky je výchozí, kapitáni používají ve výchozím stavu PTT. Mikrofon vyhodnocuje energii 20ms rámců. Držení PTT má přednost. Po uvolnění musí proběhnout 240 ms ticha před dalším automatickým přenosem. Při dvou automatických kanálech kapitán vybírá cíl v okně flotily.

Každý přijatý hráč může založit loď z přehledu flotily. Hostitel zpracuje požadavek CreateShip pod zámkem flotily se stejnou kontrolou epochy a opakovaných žádostí jako při přestupu. Hráč bez lodi nastoupí, hráč na palubě si zachová posádku i roli. Pro tuto funkci použij aktuální build také na hostiteli.

Přijatý hráč smí sám obsadit volné místo kapitána své lodi. Hostitel kontroluje členství, epochu a volné místo při zpracování žádosti. Při souběhu uspěje první platná žádost.

Katalog modelů je vložen do EXE. Výběr vychází z průniku kategorií https://starcitizen.tools/Category:Ships a https://starcitizen.tools/Category:Flight_ready načtených 22. 11. 2026, s vynecháním pozemního HoverQuadu. Textové pole zachovává ruční typy a starší šablony.
