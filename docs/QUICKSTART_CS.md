**Čeština** · [English](QUICKSTART_EN.md)

# StarComms – český návod

## Připojuji se ke kamarádům

1. Rozbal `StarComms.zip` do vlastní složky a spusť **StarComms.exe** na Windows x64. Při prvním spuštění vyber **Čeština**. Volba se uloží vedle EXE do `StarComms.cfg`.
2. Zadej svou **přezdívku**. Do pole **Adresa nebo pozvánka od hostitele** vlož celou pozvánku, kterou ti hostitel poslal. Začíná `starcomms://join`.
3. Klikni na **Připojit** a počkej, až tě hostitel přijme.
4. Klikni pravým tlačítkem na název své lodi a zvol **Přestoupit sem**. Samotné označení lodi v seznamu slouží k prohlížení. Pokud je flotila zatím prázdná, můžeš první loď přidat sám.
5. Promluv. Ve výchozím nastavení tě uslyší posádka tvé lodi.

Po úspěšném připojení můžeš při příštím spuštění vybrat **Znovu připojit** s názvem flotily. Když hostitel změní adresu nebo port, vyžádej si novou pozvánku.

## Zakládám flotilu

1. Zadej přezdívku a název flotily. Klikni na **Chci hostovat**.
2. Vyber **Pozvat hráče**. StarComms zkusí připravit připojení přes router. Jakmile uvidíš pozvánku, klikni na **Kopírovat pozvánku** a vlož ji skupině do zprávy.
3. U příchozí žádosti klikni na **Přijmout**. Během letu můžeš přepnout do hry. Ukončením hostování se flotila odpojí.

### Jakou adresu mám poslat?

- **Hrajete na stejné domácí síti.** Použij místní pozvánku. V **Podrobnosti připojení** jsou místní adresy označené **Pouze místní síť (LAN)**.
- **Kamarád je v jiné domácnosti.** Použij pozvánku s veřejnou adresou. Pokud router hlásí například `192.168.11.10`, jde o soukromou adresu. Zobrazení adresy routeru samo o sobě nepotvrzuje dostupnost z internetu.
- **Znáš svou veřejnou IP.** Otevři **Podrobnosti připojení → Veřejná IP pro pozvánky**. Zadej IP a případně vnější port ve tvaru `IP:port`. Klikni na **Zavřít**. Volba **Pozvat hráče** teď použije uloženou adresu. K automatickému výběru se vrátíš vymazáním pole a zavřením okna.

Pokud se kamarád přes veřejnou IPv4 nedostane k žádosti o přijetí, zkontroluj na routeru přesměrování **TCP i UDP** z vnějšího portu v pozvánce na místní IP hostitelského počítače a port z **Podrobnosti připojení**. Ve stejném okně můžeš přes **Povolit hostování ve Windows** nastavit firewall. Ruční zadání IP v aplikaci mění jen pozvánku. Když poskytovatel sdílí veřejnou IPv4 mezi více zákazníky (CGNAT), můžete použít dosažitelnou IPv6, pokud ji mají obě strany, nebo může flotilu založit jiný hráč.

## Lodě a kapitáni

| Co chci udělat | Jak na to |
| --- | --- |
| Založit loď | Klikni na **Přidat loď**, zadej její název a potvrď **Vytvořit loď**. Loď může přidat kterýkoli hráč. Pokud jsi bez lodi, rovnou nastoupíš. |
| Vybrat model | Otevři **Typ lodi**. Vlevo vyber výrobce a vpravo model, nebo napiš část názvu do hledání. Typ můžeš také vynechat nebo potvrdit přes **Použít vlastní typ**. |
| Přestoupit | Klikni pravým tlačítkem na cílovou loď a vyber **Přestoupit sem**. |
| Stát se kapitánem | Nastup na loď bez kapitána. Klikni pravým tlačítkem na její název a zvol **Stát se kapitánem**. |
| Předat velení | Jako kapitán vyber člena vlastní posádky a klikni na **Předat velení vybranému členovi**. Hostitel může kapitána také jmenovat ve **Správě flotily**. |
| Odebrat prázdnou loď | Klikni pravým tlačítkem na její název a vyber **Smazat prázdnou loď**. |

## Mikrofon a mluvení během hry

Ve **Zvuk a nastavení** vyber mikrofon a sluchátka. Promluv běžným hlasem a sleduj, jestli se objeví **Řeč zachycena**. Pokud začátky slov mizí, posuň práh hlasové aktivace doleva. Pokud ji spouští klávesnice, posuň ho doprava. Výstup ověříš tlačítkem **Vyzkoušet zvuk**.

Ve **Způsob vysílání** se posádka a kapitáni nastavují zvlášť. Změny potvrď přes **Hotovo**.

- **Hlasová aktivace** spustí přenos při řeči. Pokud ji jako kapitán zapneš pro oba kanály, vyber v hlavním okně, ke komu chceš mluvit.
- **Držet tlačítko** vysílá po dobu držení přiřazené klávesy nebo tlačítka. Funguje i při přepnutí do hry.
- **Výchozí kombinace pro kapitána** je hlasová aktivace pro posádku a tlačítko pro kapitány. Přiřaď si ho před letem. Podrž ho během rozkazu, pak ho uvolni a před další větou k posádce udělej krátkou pauzu.

## Když něco nefunguje

| Co se děje | Co zkusit |
| --- | --- |
| Hostitel nevidí moji žádost | Požádej ho o čerstvou pozvánku. Pokud jste ve dvou domácnostech, ověřte veřejnou adresu a port podle části o hostování. |
| Vidím flotilu, ale neslyšíme se | Ověřte, že jste nastoupili na stejnou loď, a zkontrolujte mikrofon a výstup. Pokud aplikace hlásí problém s hlasovým spojením, klikni na **Zkusit hlas znovu**. Hostitel zkontroluje průchod UDP. |
| Aplikace hlásí jiné sestavení | Porovnejte **verzi i build** vpravo dole. Pro společný let použijte stejné vydání. |
| Windows odepřel přístup ke složce profilu | Přesuň EXE se svým CFG do složky, kam můžeš zapisovat. |

Pokud problém trvá, poznamenej si číslo sestavení, přesné znění chyby a zda nastala u hostitele nebo u hráče. V nastavení můžeš zapnout **Logs on**, problém zopakovat a logování opět vypnout. Diagnostické soubory najdeš přímo vedle spuštěného `StarComms.exe`.

Soubor `StarComms.cfg` vedle EXE obsahuje celý tvůj profil včetně důvěryhodných hostitelů, šablony lodí a identity hostitele chráněné účtem Windows. Kamarádům pošli pouze EXE nebo distribuční ZIP. Při první aktualizaci se převezme dosavadní profil. Pro vlastní původní složku použij při prvním spuštění `StarComms.exe --profile "c:\cesta\k\profilu"`.
