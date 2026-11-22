# StarComms pro hráče

Po prvním úspěšném připojení najdeš na úvodní obrazovce tlačítko **Znovu připojit** s názvem flotily. Funguje i po restartu. Pokud hostitel změnil adresu nebo port, vlož novou pozvánku.

1. Rozbal **StarComms.exe** do vlastní složky na Windows x64.
2. Spusť aplikaci. Potřebný runtime obsahuje EXE.
3. Napiš přezdívku, podle které tě hostitel pozná.
4. Vlož celou pozvánku od hostitele, klikni na **Připojit** a počkej na přijetí.
5. Vyber loď a klikni na **Přestoupit sem**. Prohlížení seznamu lodí poslech nepřepíná.
6. Posádka tě slyší, když promluvíš. Pro soukromý hovor si ve **Zvuk a nastavení** přiřaď tlačítko pro kapitány. Během držení tě slyší pouze kapitáni. Po uvolnění udělej krátkou pauzu, než začneš mluvit k posádce.
7. Ve **Zvuk a nastavení → Způsob vysílání** nastav každý kanál zvlášť. Pro posádku i kapitány vyber **Hlasová aktivace** nebo **Držet tlačítko**. Při volbě tlačítka se pod kanálem zobrazí jeho přiřazení. Potvrď **Hotovo**. Při hlasové aktivaci obou kanálů vyber jako kapitán příjemce kliknutím na příslušný kanál v hlavním okně.

## Když hostuješ

Zadej přezdívku a název flotily, pak vyber **Chci hostovat**. Nad seznamem lodí klikni na **Přidat loď**. Na hlavní obrazovce klikni na **Kopírovat pozvánku** a vlož ji kamarádovi do zprávy. Pokud je adresa jen pro LAN, uvidíš to po zkopírování. Jinou adresu můžeš vybrat přes **Podrobnosti připojení**. Na hlavní obrazovce přijmi žádosti. Ve **Správa flotily → Lodě a posádky** vyber vlevo loď. Vpravo vyber člena posádky a klikni na **Jmenovat kapitánem**. Hráče z jiné lodi nebo bez lodi nejdříve přiveď na palubu přes **Přesunout na tuto loď**. V záložce **Hráči** najdeš i hráče bez lodi. **Šablona lodí** uloží názvy a modely pro další let. Nech StarComms běžet až do konce letu.

Adresa označená **Pouze místní síť (LAN)** běžně poslouží hráčům na stejném routeru. Pro kamaráda v jiné domácnosti použij veřejnou adresu. Při ručním přesměrování nastav na routeru oba protokoly **TCP i UDP** na port zobrazený aplikací. Při připojení přes IPv6 potřebují IPv6 obě strany. Pokud poskytovatel používá CGNAT a funkční IPv6 chybí, může hostování převzít spoluhráč s dosažitelnou adresou.

## Když něco nefunguje

- **Vidíš flotilu, hlas nefunguje.** Klikni na **Zkusit hlas znovu**. Pokud to nepomůže, ověř mikrofon a výstup v nastavení a požádej hostitele o kontrolu UDP.
- **Jiné sestavení.** Obě čísla najdete v upozornění. V pravém dolním rohu má každý svou verzi a build. Hostitel může ostatním poslat stejnou kopii EXE.
- **Potřebuješ diagnostiku.** V nastavení zapni **Logs on**, zopakuj problém a logování zase vypni. Soubory vznikají vedle skutečného EXE. Ve výchozím stavu je logování vypnuté.


Pokud Windows odepře zápis do výchozí složky profilu, aplikace při spuštění nabídne výběr složky. Při dalším spuštění vyber stejnou složku pro zachování identity hostitele. Pro spuštění přímo s tímto profilem použij parametr --profile následovaný úplnou cestou ke složce v uvozovkách.

## Citlivost mikrofonu a mluvčí

Ve **Zvuk a nastavení** promluv běžným hlasem a sleduj místní test mikrofonu. Bílá čára ukazuje práh spuštění. Pokud tichá slova mizí, posuň práh doleva. Pokud vysílání spouští psaní na klávesnici, posuň ho doprava. Při řeči má ukazatel hlásit **Řeč zachycena**, při běžném hluku **Ticho**. Během nastavování zůstává vysílání pozastavené. Volbu ulož přes **Hotovo**.

U mluvčího se objeví mikrofon a označení **posádka** nebo **kapitáni**. Člen posádky vidí aktivitu kanálů, které může slyšet.

## Zkouška při letu ze dvou domácností

Tři hráči použijí stejný build. Na první lodi je kapitán A a člen posádky, na druhé kapitán B. A používá hlasovou aktivaci pro posádku a tlačítko pro kapitány. Ve hře při minimalizovaném StarComms:

1. A řekne „posádka jedna“. Slyší ho jen posádka první lodi.
2. A podrží kapitánské tlačítko, řekne „velení dva“, uvolní ho a hned dořekne „konec“. Jen kapitán B slyší část během držení. Posádka neslyší soukromou větu ani její dozvuk po uvolnění.
3. A udělá krátkou pauzu a řekne „posádka tři“. Opět ho slyší posádka první lodi.

Hostitel může v hlavním okně vybrat loď a kliknout na jméno člena posádky. Tlačítkem **Jmenovat kapitánem** mu svěří velení. Pro přesun vybere cílovou loď a klikne na **Přesunout na loď**.

Každý připojený hráč může kliknout na **Přidat loď**, zadat název a potvrdit **Vytvořit loď**. Pokud jsi bez lodi, rovnou nastoupíš. Pokud už jsi na palubě, zůstaneš u své posádky. Kapitána můžeš určit kliknutím na jméno člena posádky.

Pravým tlačítkem na název lodi otevřeš nabídku **Přestoupit sem** a **Smazat prázdnou loď**. Prázdnou loď může odebrat každý připojený hráč.

Chceš velet? Nastup na loď bez kapitána, vyber její název a klikni na **Stát se kapitánem**. Stejná volba je pod pravým tlačítkem na názvu lodi. Když už loď kapitána má, může ti předat velení.

Při vytváření zadej vlastní název a v poli **Typ lodi** vyber model. Psaním začátku názvu ho rychle najdeš. Typ lze vyplnit i ručně nebo vynechat. Výběr funguje offline.

Otevři **Typ lodi**. V jedné nabídce vyber vlevo výrobce a vpravo model. Do horního pole můžeš rovnou napsat část názvu napříč výrobci; vlastní hodnotu potvrď přes **Použít vlastní typ**.
