# Rozhodnutí při implementaci

- Název aplikace, EXE, schématu pozvánky i profilu je StarComms. Identifikátor FCV1 v síťové obálce zachovává kompatibilitu se zadáním.
- Host rozhoduje o oprávnění každého rámce pod stejným zámkem jako při změně role. Jeho vlastní hlasový klient používá stejný šifrovaný přenos jako ostatní.
- Schannel na testovacím Windows vyžadoval uživatelský kontext soukromého klíče. Pro trvalou identitu aplikace chrání exportovaný klíč pomocí DPAPI aktuálního uživatele.
- Při uvolnění PTT se zahodí čekající nezakódované vzorky. Kodér pracuje mimo zámek audio callbacku a před odesláním znovu ověří aktivní stisk.
- Windows testy otevírají vlastní kopie EXE s explicitními dočasnými profily pomocí přepínače `--test-profile`. Běžné spuštění používá LocalAppData. Místo diagnostických logů se odvozuje od cesty procesu i při testování.
- Publikace používá výsledek předchozího uzamčeného restore. Zabrání se tak přepsání zámků knihoven odlišným RID při druhém restore během publish.
- Hlasová aktivace posádky je výchozí, kapitáni používají ve výchozím stavu PTT. Mikrofon vyhodnocuje energii 20ms rámců. Držení PTT má přednost. Po uvolnění musí proběhnout 240 ms ticha před dalším automatickým přenosem. Při dvou automatických kanálech kapitán vybírá cíl v okně flotily.
