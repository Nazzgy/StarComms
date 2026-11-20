# StarComms

Hlasová aplikace pro flotilu až dvanácti hráčů ve Star Citizenu. Hostitel vede flotilu ze stejné aplikace jako ostatní. Posádky mluví po jednotlivých lodích a kapitáni mají druhý kanál.

## Pro hráče

Z distribuční složky vezmi pouze [StarComms.exe](artifacts/StarComms.exe). Aplikace pro Windows x64 obsahuje vlastní .NET runtime. Připojení přes internet vyžaduje přímou dosažitelnost počítače hostitele přes TCP i UDP.

[Český návod](docs/QUICKSTART_CS.md) · [Akceptační testy a otisk EXE](docs/IMPLEMENTATION_STATUS.md)

## Ze zdrojů

Na Windows x64 použij SDK určené v `global.json` a PowerShell. První obnova stáhne balíčky. Další sestavení ověřuje uzamčené verze závislostí.

```powershell
./scripts/bootstrap.ps1
./scripts/publish.ps1
```

Publikační skript sestaví řešení v režimu Release, spustí xUnit, ověří jedno EXE a jeho číslo sestavení a zapíše výsledek do `artifacts/StarComms.exe`.

- `src/StarComms.Core` obsahuje členství, lodě, oprávnění a automat PTT.
- `src/StarComms.Engine` zajišťuje TLS, šifrované UDP, Opus a zvukový mix.
- `src/StarComms.App` obsahuje české WPF rozhraní, WASAPI, globální vstup, síťová nastavení a diagnostiku.
- `tests` a `tools` obsahují automatické testy, kontrolu přenosu a simulátor dvanácti hráčů. Příkazy jsou v přehledu akceptace.

[Specifikace](STARCOMMS_SPEC.md) · [Implementační rozhodnutí](docs/DECISIONS.md)
