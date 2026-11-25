# Multiplayer quickload — handover

Branch: `feature/multiplayer-quickload`, based on upstream `master` at `ed6d8ab5`.

Read these first:

- Design: [specs/2026-11-24-multiplayer-quickload-design.md](specs/2026-11-24-multiplayer-quickload-design.md)
- Plan, spike results and the in-game checklist: [plans/2026-11-24-multiplayer-quickload.md](plans/2026-11-24-multiplayer-quickload.md)

## What it does

With the server setting `AllowQuickLoad` on and warp mode `Subspace`:

- **F5** in flight stores a snapshot of the vessel you fly, with the game time.
- **F9** (held for one second) removes that vessel and everything that came off it since F5, reloads the flight scene on the saved vessel under a new vessel id, and moves you alone into a new subspace at the saved time. Other players are not affected.

## State

- Code is written and committed. Unit tests pass: `LmpCommonTest` 34 passed / 1 skipped, `ServerTest` 27 passed.
- The client compiles. **None of the client quickload code has ever run in the game.** Treat everything in `LmpClient/Systems/QuickLoad/` as unverified.
- A fresh reviewer checked the branch once; its four important findings are fixed in `a90fa2e9`. Two of those fixes (failure safety of `DoQuickLoad`, the subspace handshake) have no test, because the code is all KSP calls.

## Next step

Run the ten checks in the plan's "Test results" table with two KSP clients and a local server, and record the results there. Checks 1–3 also answer the open spike questions Q2–Q4 (does rewinding the clock work, is the fresh vessel id enough, do other clients see one vessel). If Q2 or Q3 fails, the design needs rework, not a patch.

## Where the code is

| Part | Files |
|---|---|
| Server settings | `Server/Settings/Definition/WarpSettingsDefinition.cs`, `Server/Message/SettingsMsgReader.cs`, `LmpCommon/Message/Data/Settings/SetingsReplyMsgData.cs` |
| Rules without KSP types (unit tested) | `LmpCommon/QuickLoad/QuickLoadFamily.cs`, `LmpCommon/QuickLoad/QuickLoadCheck.cs`, tests in `LmpCommonTest/QuickLoadTests.cs` |
| Client system | `LmpClient/Systems/QuickLoad/` (`QuickLoadSystem.cs` holds F5, F9 and `DoQuickLoad`) |
| Hooks in existing code | `WarpSystem.HoldSubspaceRequest`, `VesselRemoveMessageHandler` (remote recovery clears the snapshot), `ScreenText.cs` (English texts only) |

## Building

Requires the .NET 10 SDK (`global.json` pins 10.0.100).

Server and tests:

```bash
dotnet test LmpCommonTest/LmpCommonTest.csproj
dotnet test ServerTest/ServerTest.csproj
dotnet publish Server/Server.csproj -c Debug -o <output folder>
```

Client (`LmpClient`, .NET Framework 4.7.2):

- `External/KSPLibraries/KSPLibraries.7z` is password protected. Instead, copy these DLLs from your own KSP 1.12 install (`KSP_Data/Managed`, on macOS `KSP.app/Contents/Resources/Data/Managed`) into `External/KSPLibraries/`. They are git-ignored and must not be committed:
  `Assembly-CSharp`, `System`, `System.Xml`, `UnityEngine`, `UnityEngine.AnimationModule`, `UnityEngine.CoreModule`, `UnityEngine.IMGUIModule`, `UnityEngine.ImageConversionModule`, `UnityEngine.InputLegacyModule`, `UnityEngine.PhysicsModule`, `UnityEngine.TextRenderingModule`, `UnityEngine.UI`, `UnityEngine.UnityWebRequestModule`.
- On Windows with Visual Studio the project builds as usual. On macOS or Linux there are no .NET Framework reference assemblies; get the NuGet package `Microsoft.NETFramework.ReferenceAssemblies.net472` (1.0.3) into the NuGet cache and build with:

```bash
dotnet msbuild LmpClient/LmpClient.csproj -verbosity:minimal -p:TargetFrameworkRootPath=$HOME/.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/
```

To make an installable mod folder, take `GameData/LunaMultiplayer` from an official LMP release and replace `LmpClient.dll`, `LmpCommon.dll`, `LmpGlobal.dll`, `Lidgren.Network.dll` and `CachedQuickLz.dll` in `Plugins` with the ones from `LmpClient/bin/Debug`, and `LunaMultiplayer.version` with the one from the repo root. The other DLLs in `Plugins` stay.

## Test setup

- Server and every client must be this build. The settings message has two new fields, so mixing with upstream builds does not work.
- Start the server once so it writes its config, then set `AllowQuickLoad` to `true` in `Config/WarpSettings.xml`. `QuickLoadMaxRewindMinutes` defaults to 60.
- The server registers with the public master server list by default (`RegisterWithMasterServer` in `Config/MasterServerSettings.xml`); switch that off for a private test.
- The second client is a second copy of KSP with another player name.

## Decisions made during implementation

Each of these differs from, or goes beyond, the plan. They were made without the project owner and can be reversed.

- **F9 is read by `QuickLoadSystem` itself.** Stock never calls its quickload while `Flight.CanQuickLoad` is false, so the system holds an input lock on `ControlTypes.QUICKLOAD` and watches `GameSettings.QUICKLOAD` with a one second hold. The existing Harmony prefixes are unchanged.
- **F5 uses `GameEvents.onGameAboutToQuicksave`**, not a Harmony patch. It may also fire for Alt+F5 named saves, which would then replace the snapshot.
- **The swap reloads the flight scene** (`FlightDriver.StartAndFocusVessel` on `HighLogic.CurrentGame.Updated()`), instead of swapping in place. This is what stock does and it works after a crash. Cost: a loading screen on every F9.
- **Planetarium time is not set before the reload**; the reload takes it from `flightState.universalTime`. If the clock is not rewound after F9, look here first.
- **Spike questions Q2–Q4 were not spiked** with throwaway code; the first in-game session answers them.
- **New texts exist only as English defaults** in `ScreenText.cs`; the XML language files are untouched.
- **With `AllowQuickLoad` off the system hooks nothing**, so behaviour is as upstream.
- **No operator documentation was added**, because the repo has none for server settings; the settings carry an `XmlComment` in the generated file.

## Known open points

- **F9 while flying a different vessel** than the quicksaved one currently switches you to the saved vessel. Whether to refuse instead is undecided.
- The stock docking UI mode can clear the input lock; stock's "quickload disabled" text then shows next to a working F9. The lock is also held outside flight.
- Stock may renumber `persistentId` of the restored vessel and parts; references by `persistentId` are not remapped.
- The two settings fields were inserted mid-message and `LmpVersioning` was not bumped, so an upstream client with the same version number misreads this server's settings.
- No `ServerTest` checks that the settings are sent on a non-custom difficulty.
- `QuickLoadSnapshot.VesselId` is only logged; the csproj entries sit in the Events block.
- The limits are enforced by the client only; a modified client can ignore them (see the design, section "Server").
