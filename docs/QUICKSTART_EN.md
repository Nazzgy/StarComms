[Čeština](QUICKSTART_CS.md) · **English**

# StarComms player and host guide

## Joining a fleet

Extract the ZIP into a folder you can write to. Run **StarComms.exe**, choose **English**, enter your nickname and paste the complete invitation into **Host address or invitation**. It starts with `starcomms://join`. Click **Connect**. Once the host accepts, right-click a ship and select **Board this ship**. Selecting its name alone lets you inspect it while continuing to hear your current crew.

After your first successful connection, **Reconnect** takes you back to the last fleet. Ask for a fresh invitation if the address or port changes.

## Hosting

1. Enter your nickname and fleet name and select **Host a fleet**.
2. Select **Invite players → Copy invitation**, then paste the link into your group chat. Click **Accept** as each player requests to join.
3. Keep StarComms running throughout the flight. Stopping the host disconnects the fleet.

A local invitation works for players on your home network. Friends in other homes need a reachable public address. If you know yours, enter it under **Connection details → Public IP for invitations (optional port)**. Close the window to save. **Invite players** will use the saved address. Clear the field to restore automatic selection.

If the host never sees a request, check the address in the invitation. For manual IPv4 forwarding, forward the external port in the invitation over **both TCP and UDP** to the host PC and port shown in **Connection details**. **Allow hosting in Windows** configures the host firewall. Entering a public IP in the app changes only the invitation. With carrier-grade NAT, try reachable IPv6 if both players have it, or let another player host.

## Ships and captains

- **Create a ship.** Anyone can use **Add ship**. Enter a name, optionally choose a manufacturer and model or search by name, then select **Create ship**. Players without a ship automatically board the ship they create.
- **Board or take command.** Right-click a ship and select **Board this ship**. A crew member on a ship without a captain can use **Become captain**. A captain can select a crew member and hand over command.
- **Delete an empty ship.** Right-click it and select **Delete empty ship**. Hosts can also move players or appoint captains in **Fleet management**.

## Audio and troubleshooting

Under **Audio and settings**, select your microphone and headphones. Speak normally and check for **Speech detected**. Move the threshold left if quiet speech gets cut off, or right if the keyboard triggers transmission. Select **Test sound** to check your output.

Each channel supports **Voice activation** and **Push to talk**. The default is voice activation for crew and push-to-talk for captains. Assign your captain button before the flight. Hold it to speak to other captains, release it, then pause briefly before speaking to your crew. If both channels use voice activation, select the intended channel in the main window. Select **Done** to finish.

If everyone is connected but silent, check ship membership and audio devices. If voice verification failed, use **Retry voice** and check UDP on the host. For a build mismatch, compare both build numbers in the lower-right corner. For troubleshooting, enable **Logs on**, reproduce the issue and turn logging off. Find the logs beside the EXE.

To change languages, close StarComms and set `language=cs` or `language=en` in **StarComms.cfg** beside the EXE. Remove only the language line to be asked again.

The CFG beside the EXE contains your profile, including trusted hosts, the ship template and the host identity protected by your Windows account. Share only the EXE or release ZIP. On first upgrade the app imports your previous profile. For a custom legacy folder, start once with `StarComms.exe --profile "c:\path\to\profile"`.
