**Čeština** · [English](README.en.md)

# StarComms

StarComms je česko-anglická hlasová aplikace pro společné lety ve Star Citizenu na Windows x64. Každá loď má vlastní hovor pro posádku. Kapitáni mají navíc společný kanál, takže se mohou domluvit na dalším manévru, zatímco posádky dál mluví na svých lodích.

**[Stáhnout StarComms pro Windows](https://github.com/Nazzgy/StarComms/releases/latest)** · **[Český návod pro hráče a hostitele](docs/QUICKSTART_CS.md)**

## První let

1. V sekci **Assets** u vydání stáhni `StarComms.zip`, rozbal ho a spusť `StarComms.exe`. Pokud sis stáhl přímo EXE, stačí ho spustit. Vše potřebné je uvnitř.
2. Jeden hráč zadá přezdívku a vybere **Chci hostovat → Pozvat hráče**. Pozvánku pošle skupině třeba na Discord.
3. Ostatní vloží pozvánku do StarComms a kliknou na **Připojit**. Hostitel jejich žádosti přijme.
4. Přes **Přidat loď** může kdokoli založit loď. Ostatní na ni kliknou pravým tlačítkem a vyberou **Přestoupit sem**. Od té chvíle spolu posádka může mluvit.

Při prvním spuštění vyber **Čeština** nebo **English**. Volbu si aplikace zapamatuje v souboru `StarComms.cfg` vedle EXE. Pro změnu aplikaci zavři, nastav `language=cs` nebo `language=en` a spusť ji znovu. Pro nové zobrazení výběru odeber pouze řádek `language`.

## Co se hodí vědět před letem

- **Hlas během hry.** Posádka má ve výchozím nastavení hlasovou aktivaci. Pro kapitánský kanál si přiřaď tlačítko. Každý kanál můžeš samostatně přepnout mezi hlasovou aktivací a push-to-talk.
- **Velikost flotily.** Počet hráčů nemá pevně stanovený strop. Místní test ověřil připojení 26 účastníků včetně hostitele. Při letu přes internet záleží také na připojení hostitele.
- **Hostování.** Flotila běží přímo v aplikaci jednoho hráče. Ten ji nechá otevřenou po celý let. Ke spojení dvou domácností potřebují hráči dosáhnout na jeho počítač přes TCP i UDP. Postup pro veřejnou IP a router najdeš v českém návodu.
- **Diagnostika.** Logování je ve výchozím stavu vypnuté. Přepínač **Logs on** zapisuje diagnostické soubory vedle spuštěného EXE. Verzi a číslo sestavení najdeš vpravo dole.

## Sestavení ze zdrojů

Na Windows x64 nainstaluj .NET SDK ve verzi uvedené v [global.json](global.json). V PowerShellu z kořene projektu spusť následující příkazy.

```powershell
./scripts/bootstrap.ps1
./scripts/publish.ps1
```

První příkaz obnoví balíčky. Druhý sestaví aplikaci, spustí testy a vytvoří samostatný soubor `artifacts/StarComms.exe`.

[Historie změn](CHANGELOG.md) · [Výsledky testů](docs/IMPLEMENTATION_STATUS.md) · [Technická specifikace](STARCOMMS_SPEC.md)

Soubor `StarComms.cfg` vedle EXE obsahuje celý tvůj profil včetně důvěryhodných hostitelů, šablony lodí a identity hostitele chráněné účtem Windows. Kamarádům pošli pouze EXE nebo distribuční ZIP. Při první aktualizaci se převezme dosavadní profil. Pro vlastní původní složku použij při prvním spuštění `StarComms.exe --profile "c:\cesta\k\profilu"`.
