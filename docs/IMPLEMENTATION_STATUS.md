# StarComms pro Windows x64 — stav ověření

Distribuční soubor **artifacts/StarComms.exe** má verzi **0.1.0+20261120.101757**, velikost **142035400 bajtů** a SHA-256 **c1d72ea84a2f05ab65d428908d038ad46e20ebce109b0008d7a7e9de2451b30c**. Adresář obj/publish/win-x64 při kontrole obsahoval jen StarComms.exe. EXE je samostatná WPF aplikace s přibaleným runtime.

Základ pracovního stromu je commit 073e5eee0489c71fcd0422cc1fa9211942523a6e. Místní změny nejsou commitnuty. Windows x64 10.0.26200, .NET SDK 10.0.400. NuGet restore dokončil uživatel. Lokální build následně použil obnovené balíčky.

## Ověření hlasových kanálů

Tři klienti, dvě lodě, místní TCP/TLS a šifrované UDP: posádka přijala hlasové úseky 1 a 3. Soukromý úsek 2 přijal druhý kapitán. Pokračující řeč po uvolnění kapitánského PTT zůstala zablokovaná až do pauzy. Test používá syntetický signál. Dvě spuštěná publikovaná EXE navíc ověřila živý přenos z WASAPI přes PTT. UI test nastavil práh na −48 dBFS a ověřil jeho uložení. Star Citizen a dvě domácí sítě: NOT RUN.

## Výsledky

- Předchozí sestavení na Windows otevřelo WASAPI vstup i výstup. Druhé EXE přijalo rámce z mikrofonu po stisku PTT v okně. Člověk srozumitelnost řeči neposuzoval. Podrobnosti v [windows-voice.txt](windows-voice.txt).
- Dvě odlišná sestavení zobrazila po spojení varování s oběma verzemi. Podrobnosti v [windows-mixed.txt](windows-mixed.txt).
- Poslední dokončený VSTest běh vykázal 53 z 53 úspěšných testů. [xunit.txt](xunit.txt) obsahuje výsledky z TRX. Dalších 37 kontrol domény a transportu je v [transport-run.txt](transport-run.txt).
- Minutová simulace dokončila 33 884 odeslání a 92 988 doručení. Záměrně zahodila 1 797 vstupních rámců. Počet neoprávněných příjemců byl 0, maximální hloubka jitter bufferu 6. [sim-60s.txt](sim-60s.txt)
- Třicetiminutová simulace běžela se dvanácti klienty, třemi loděmi, změnami kapitánů, jitterem a přibližně pětiprocentní ztrátovostí. Poslední doložené měření po 21 min 51 s zachytilo 2 049 964 doručení, 0 neoprávněných doručení a hloubku nejvýše 6. Při změně pracovního prostředí zmizela běžící relace. Dokončení 30 minut není doloženo. Starší simulátor zapisoval desetinnou čárku v sekundách i CPU. [soak.txt](soak.txt)

## Správa flotily

Kontrola ovládacích prvků WPF bez snímání obrazovky ověřila výběr výchozí lodi, nalodění, jmenování kapitána, obnovu seznamů po příchodu hráče a ochranu hostitele před vyřazením. Vizuální kontrola neproběhla.

## Aktuální sestavení

Publikované EXE obsahuje aktuální zdroje a tmavý motiv. Předchozí EXE s hlasovou aktivací na Windows otevřelo hlavní okno. Nové rozložení prošlo kompilací WPF a kontrolou vazeb. Vizuální kontrola nového rozložení neproběhla. Testy VoiceActivationTests ověřily kombinace obou režimů a automatické směrování syntetického signálu. Živý rozhovor s hlasovou aktivací, hra a dvě domácí sítě v tomto sestavení: NOT RUN. Skript pro automatické ovládání nastavení Windows odmítl spustit (odepřený přístup).

## Reprodukce na Windows

Z kořene projektu použij PowerShell a SDK z global.json. Následující příkazy sestaví aktuální zdroje a při každé nové publikaci vytvoří nové číslo sestavení.

```powershell
./scripts/bootstrap.ps1
./scripts/publish.ps1
. ./scripts/environment.ps1
dotnet run --project tools/StarComms.TransportChecks
dotnet run --project tools/StarComms.Sim
dotnet run --project tools/StarComms.Sim -- --soak
./scripts/test-windows-ui.ps1 -Executable (Resolve-Path artifacts/StarComms.exe) -HostFlow
Get-FileHash artifacts/StarComms.exe -Algorithm SHA256
```

Pro test odlišných verzí přidej UI skriptu -OtherBuild s absolutní cestou ke kompatibilnímu staršímu EXE. Ověřené EXE vzniklo přes dotnet publish src/StarComms.App --no-restore -c Release -p:BuildId=20261118.232415 -m:1 -o artifacts/publish/win-x64.

## Akceptační matice

PASS označuje ověřený scénář. ČÁST označuje konkrétní pozorovanou část scénáře. NOT RUN označuje chybějící ověření.

| Test | Stav | Rozsah |
|---|---|---|
| CON-001 | PASS | Dvě samostatné aplikace, čisté testovací profily, schválení a nástup na Polaris. Windows UI + IPv6LoopbackWithPinnedCertificate. |
| CON-002 | ČÁST | IPv4, IPv6, rozsah portů, nejednoznačné zápisy a pozvánky pokrývá CoreTests. |
| CON-003 | ČÁST | Zrušení žádosti uklidilo čekatele. Odmítnutí nezaložilo členství. |
| HOST-001 | PASS | Obsazený UDP port, vyčerpání rozsahu a přechod na další společný TCP/UDP port. |
| HOST-002 | NOT RUN | Celý samostatný scénář nebyl ověřen. |
| HOST-003 | PASS | RouterTests ověřily obsazený port, úklid částečného mapování, odlišný přidělený port a neúspěšnou obnovu lease. |
| HOST-004 | ČÁST | IPv4 a IPv6 fungovaly na loopbacku. |
| HOST-005 | PASS | Test s průchozím TCP zablokoval UDP, poté povolil UDP a ověřil nový probe. |
| HOST-006 | NOT RUN | Celý samostatný scénář nebyl ověřen. |
| HOST-007 | ČÁST | Potvrzené ukončení hostování ve skutečném okně. |
| AUTH-001 | NOT RUN | Celý samostatný scénář nebyl ověřen. |
| AUTH-002 | NOT RUN | Celý samostatný scénář nebyl ověřen. |
| AUTH-003 | PASS | Host s jedenácti klienty. Dvě další souběžné žádosti byly odmítnuty. |
| TLS-001 | ČÁST | Shodný a odlišný otisk v CoreTests, připojení přes připnutou pozvánku ve Windows. |
| TCP-001 | ČÁST | Čtení po jednotlivých bajtech a odmítnutí neplatných délek. |
| TCP-002 | ČÁST | Opakování ID po provedení mutace vrátilo původní výsledek. Nový požadavek se starou epochou byl odmítnut. |
| TCP-003 | NOT RUN | Celý samostatný scénář nebyl ověřen. |
| UDP-001 | ČÁST | Round trip a změna každého bitu paketu. |
| UDP-002 | PASS | Opakované a přeházené pakety, staré sekvence a neplatný tag. |
| UDP-003 | ČÁST | Souběžná kontrola jedinečných sekvencí. |
| UDP-004 | NOT RUN | Celý samostatný scénář nebyl ověřen. |
| UDP-005 | ČÁST | Jednotkové testy chybných formátů. |
| UDP-006 | NOT RUN | Celý samostatný scénář nebyl ověřen. |
| UDP-007 | ČÁST | VoiceEnd před prvním rámcem, platnost okna a staré bursty v CoreTests. |
| ROUTE-001 | PASS | Každý ze dvanácti hráčů poslal rámec své lodi. Test porovnal skutečnou množinu příjemců. |
| ROUTE-002 | PASS | Všichni tři kapitáni postupně promluvili. Každý rámec obdrželi dva správní příjemci. |
| ROUTE-003 | PASS | Po výměně kapitána hostitel jako posádka nedostal velitelský ani cizí lodní rámec. |
| ROUTE-004 | NOT RUN | Celý samostatný scénář nebyl ověřen. |
| ROLE-001 | ČÁST | Revokace práv v doméně, změny kapitánů za provozu v dlouhé simulaci. |
| ROLE-002 | ČÁST | Přestup odebral velení. Ověřena odmítnutá stará epocha. |
| ROLE-003 | ČÁST | Nástup ve skutečném GUI, jednotkový test smazání obsazené lodi. |
| ROLE-004 | ČÁST | Odmítnutí předání neoprávněným členem. |
| PTT-001 | ČÁST | Automat stisk/uvolnění a přenos při stisku ve skutečném okně. |
| PTT-002 | PASS | Obě pořadí stisku, opakování a uvolnění všech tlačítek. |
| PTT-003 | ČÁST | Jednotkový test drženého vstupu po odzbrojení. |
| PTT-004 | ČÁST | Stisk na tlačítku aplikace doručil audio. Herní fokus, fyzická boční myš, zamknutí a suspend neověřeny. |
| AUD-001 | PASS | Skutečný Opus 48 kHz, mono, 960 vzorků. Dekódovaný testovací signál měl nenulové vzorky. |
| AUD-002 | PASS | Jednotkový test přeházení, duplicit, výpadku a návratu. Ztráty a hloubka front v simulaci. |
| AUD-003 | PASS | Měření změny úrovně při ztišení, návratu hlasitosti, limitace a hlasitosti jednotlivého člena. |
| AUD-004 | ČÁST | Skutečné WASAPI zařízení, tón a cesta mikrofon → PTT → druhá aplikace. Chybí dva počítače, poslech člověkem a fyzické odpojení zařízení. |
| AUD-005 | ČÁST | Ukazatel mikrofonu a test tónu v nastavení. Samostatný čítač paketů mimo PTT nebyl ověřen. |
| AUD-006 | PASS | Opakované krátké bursty s novým kodérem a dekodérem. |
| REC-001 | ČÁST | Automatický návrat ke stejnému členu a lodi po přerušení TCP. |
| REC-002 | PASS | Přerušení spojení kapitána, jmenování náhrady a obnovení původního člena bez kapitánských práv. |
| REC-003 | NOT RUN | Celý samostatný scénář nebyl ověřen. |
| REC-004 | ČÁST | Dobrovolný odchod hráče i ukončení hostitele na Windows. |
| REC-005 | NOT RUN | Celý samostatný scénář nebyl ověřen. |
| UX-001 | ČÁST | Celý průchod hráče provedla Windows UI Automation. Uživatelský test se začátečníkem neproběhl. |
| UX-002 | NOT RUN | Celý samostatný scénář nebyl ověřen. |
| VER-001 | ČÁST | Assembly a úvodní okno používají shodný build. Kontrola všech obrazovek nebyla provedena. |
| VER-002 | PASS | Dvě různá publikovaná EXE se spojila a klient v okně zobrazil přesná čísla obou sestavení. |
| VER-003 | ČÁST | Samostatná kontrola chybějícího release build ID v release-version-guard.txt. |
| GAME-001 | NOT RUN | Star Citizen nebyl použit. |
| NET-001 | NOT RUN | Druhá domácí síť nebyla k dispozici. |
| LOAD-001 | ČÁST | 60 sekund dokončeno, viz sim-60s.txt. Delší běh má poslední měření v čase 1311,1 s, bez konečného PASS. |
| LOAD-002 | NOT RUN | Skupina skutečných hráčů během hry nebyla k dispozici. |
| EXE-001 | ČÁST | Kopie samotného EXE spuštěna na Windows s SDK. Čistý systém bez .NET nebyl k dispozici. |
| EXE-002 | ČÁST | Studená extrakční cache, spuštění, nastavení a zvuk prošly. Počítač nebyl fyzicky odpojen od internetu. |
| EXE-003 | ČÁST | Cesta s mezerou a diakritikou, oddělené dočasné profily. Složka EXE při testu umožňovala zápis. |
| EXE-004 | PASS | Dvě kopie publikovaného EXE, hlas z mikrofonu při stisku v okně, hostování, přijetí a vstup na loď. |
| EXE-005 | ČÁST | Jediný soubor v distribuci. Test zavřel oba procesy. Samostatný audit autostartu nebyl proveden. |
| LOG-001 | ČÁST | Čistý profil, přepínač vypnutý, vedle EXE před zapnutím žádný log. |
| LOG-002 | PASS | Windows UI test spuštěný s jinou pracovní složkou našel log vedle skutečného EXE. |
| LOG-003 | ČÁST | Vypnutí uzavřelo soubor a další událost se nezapsala. |
| LOG-004 | ČÁST | Test chyby při otevření cíle, vypnutí logování a jedna chybová událost. Fyzicky plný disk nebyl testován. |
| LOG-005 | ČÁST | Test s nižším limitem rotace, zatížení fronty, ochrana cizího souboru, filtrování hodnot a nejvýše pět výstupů. |
| LOG-006 | NOT RUN | Celý samostatný scénář nebyl ověřen. |

## Kontroly u hráčů

Na čistých Windows bez SDK zkopíruj jen EXE do prázdné složky a spusť je bez internetu. Pro mezisíťový test založ flotilu v jedné domácnosti a ve druhé vlož pozvánku. Během hry ověř držení a uvolnění obou vazeb se Star Citizenem v popředí, srozumitelnost obou hlasových kanálů a změnu mikrofonu. Tyto kontroly zůstávají neprovedené.

Ověření zápisu profilu: uložení a načtení nastavení, opětovné otevření identity DPAPI a místní připojení k hostiteli přes TLS prošly.

Hlasový panel: zkontrolovány vypočtené barvy zakázaných tlačítek WPF a jejich rozměry bez snímání obrazovky.
