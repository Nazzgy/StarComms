[Čeština](README.md) · **English**

# StarComms

StarComms is a Windows x64 voice app for Star Citizen fleets. Each ship has its own crew channel. Captains also share a command channel, so they can coordinate a maneuver while their crews keep talking aboard their own ships.

**[Download StarComms for Windows](https://github.com/Nazzgy/StarComms/releases/latest)** · **[Player and host guide](docs/QUICKSTART_EN.md)**

## Your first flight

1. Under **Assets** on the release page, download `StarComms.zip`, extract it and run `StarComms.exe`. If you downloaded the EXE directly, just run it. The app includes its runtime.
2. Choose **English** or **Čeština** at first launch. StarComms saves your choice in `StarComms.cfg` beside the EXE.
3. One player enters a nickname, selects **Host a fleet → Invite players** and shares the invitation with the group.
4. Everyone else pastes the invitation into StarComms, clicks **Connect** and waits for the host to accept.
5. Anyone can select **Add ship**. To join its crew, right-click the ship and select **Board this ship**. You can now talk to the players aboard.

## Before you fly

- **Voice in game.** Crew voice defaults to voice activation. Assign a push-to-talk button for the captains’ channel. Both channels can be configured independently.
- **Fleet size.** There is no fixed player cap. A local test connected 26 participants including the host. Internet performance also depends on the host’s connection.
- **Hosting.** The fleet runs on one player's PC. Keep the app open during the flight. Players joining from another home need to reach the host over both TCP and UDP. The guide covers public addresses and port forwarding.
- **Language.** To switch later, close the app, set the language line in `StarComms.cfg` to `language=en` or `language=cs`, then restart. Removing just the language line brings back the picker.
- **Diagnostics.** Logging is off by default. **Logs on** writes diagnostic files beside the running EXE. Your version and build number appear in the lower-right corner.

## Build from source

On Windows x64, install the .NET SDK version listed in [global.json](global.json). From the project root, run the following commands in PowerShell.

```powershell
./scripts/bootstrap.ps1
./scripts/publish.ps1
```

The first command restores packages. The second builds the app, runs the tests and produces a standalone `artifacts/StarComms.exe`.

[Changelog](CHANGELOG.md) · [Test results](docs/IMPLEMENTATION_STATUS.md) · [Technical specification](STARCOMMS_SPEC.md) (in Czech)

The CFG beside the EXE contains your profile, including trusted hosts, the ship template and the host identity protected by your Windows account. Share only the EXE or release ZIP. On first upgrade the app imports your previous profile. For a custom legacy folder, start once with `StarComms.exe --profile "c:\path\to\profile"`.
