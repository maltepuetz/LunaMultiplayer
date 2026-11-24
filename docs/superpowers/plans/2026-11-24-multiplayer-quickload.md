# Multiplayer Quickload (Personal Rewind) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** F5 stores the vessel a connected player is flying; F9 puts it back and moves that player into a subspace of their own at the saved game time.

**Architecture:** Two server settings travel to the client in the existing settings reply. Decision logic with no KSP types (which vessels belong to the saved vessel's family, and whether F9 must be refused) lives in `LmpCommon` and is unit tested. A new client system `QuickLoadSystem` holds the snapshot and performs the rewind by reusing the existing warp-stopped subspace request. The server's subspace and vessel code is not changed.

**Tech Stack:** C#; `LmpCommon` (netstandard2.0/net472/net10.0), `Server` (net10.0), `LmpClient` (.NET Framework 4.7.2, KSP + Harmony); MSTest in `LmpCommonTest` and `ServerTest`.

**Spec:** `docs/superpowers/specs/2026-11-24-multiplayer-quickload-design.md`

## Global Constraints

- Work on a branch `feature/multiplayer-quickload`, not `master`.
- `AllowQuickLoad` defaults to `false`. With it off, behaviour is exactly as today, including the F9 refusal text `CannotLoadGames` and all warp-stop handling.
- `QuickLoadMaxRewindMinutes` defaults to `60`; `0` means no limit.
- Feature is active only when `AllowQuickLoad` is true and `WarpMode == WarpMode.Subspace`.
- No change to `Server/System/WarpSystem*.cs`, `VesselStoreSystem`, `VesselContext.RemovedVessels` handling, or `Subspace.txt`.
- Flight scene only. `KSCPauseMenu_QuickLoad.cs` stays as it is. `Flight.CanQuickLoad` stays `false` unless Task 3 proves stock needs it.
- One in-memory snapshot per session. Nothing is written to disk by LMP.
- Never touch another player's vessel, lock or clock. Never restore a second copy of a vessel, part or kerbal.
- The server and both clients must be built from the same commit (the settings message layout changes).

## Before you start

This machine has no .NET SDK, and the KSP assemblies are packed in `External/KSPLibraries/KSPLibraries.7z`.

- [ ] Install the .NET 10 SDK (`global.json` pins `10.0.100`, roll-forward `latestFeature`).
- [ ] Extract `External/KSPLibraries/KSPLibraries.7z` in place. If it asks for a password, stop and ask Malte.
- [ ] Baseline: `dotnet test LmpCommonTest/LmpCommonTest.csproj` and `dotnet test ServerTest/ServerTest.csproj` pass; `dotnet msbuild LmpClient/LmpClient.csproj -verbosity:minimal` builds. If the client does not build on macOS, say so; Tasks 1–2 can still be done and tested, Tasks 3–7 need a machine that builds the client and runs KSP.

Tasks 3 and 7 need a person at the game with two KSP clients and a local server. An agent cannot do them; hand them to Malte with the checklist.

## Review Focus

1. **F9 after a crash, with no active vessel** (`FlightGlobals.ActiveVessel` null or dead, results dialog open): quickload must still restore the vessel. Test: Task 6 step 1 (`Decide` does not depend on an active vessel) and Task 7 check 2.
2. **F9 pressed again before the server has answered the first one:** must be refused, not send a second subspace request. Test: Task 2 `Refuses_WhileWaitingForSubspaceId`.
3. **`QuickLoadMaxRewindMinutes` of 0 or negative:** treated as no limit, never as "always too old". Test: Task 2 `ZeroOrNegativeLimit_MeansNoLimit`.
4. **F5 while on EVA:** the "vessel" is a kerbal; restoring it would duplicate or resurrect a kerbal outside the crew checks. F5 is refused on EVA. Test: Task 4 manual check, Task 7 check 5.
5. **Disconnect or scene change to the main menu between F5 and F9:** the snapshot must be gone on the next connection. Test: Task 5 step 3 (`OnDisabled` clears), Task 7 check 5.

---

### Task 1: Server settings reach the client

**Files:**
- Modify: `Server/Settings/Definition/WarpSettingsDefinition.cs`
- Modify: `LmpCommon/Message/Data/Settings/SetingsReplyMsgData.cs` (fields, `InternalSerialize`, `InternalDeserialize`, `InternalGetMessageSize`)
- Modify: `Server/Message/SettingsMsgReader.cs` (unconditional block, next to `msgData.WarpMode`)
- Modify: `Server/Web/Structures/Settings/ServerWarpSettings.cs`
- Modify: `LmpClient/Systems/SettingsSys/SettingsServerStructure.cs`, `LmpClient/Systems/SettingsSys/SettingsMessageHandler.cs` (next to line 19)
- Test: `LmpCommonTest/SerializationTests.cs`

**Interfaces:**
- Produces: `WarpSettingsDefinition.AllowQuickLoad` (bool, default `false`), `WarpSettingsDefinition.QuickLoadMaxRewindMinutes` (int, default `60`), each with an `[XmlComment]` like `WarpMode` has; `SettingsReplyMsgData.AllowQuickLoad` (bool), `SettingsReplyMsgData.QuickLoadMaxRewindMinutes` (int); client `SettingsSystem.ServerSettings.AllowQuickLoad` (bool) and `.QuickLoadMaxRewindMinutes` (int).

- [ ] **Step 1: Write the failing test** `SettingsReply_RoundTripsQuickLoadSettings` in `SerializationTests.cs`, following the existing tests' way of creating, serializing and deserializing a message. Set `AllowQuickLoad = true`, `QuickLoadMaxRewindMinutes = 45`, `WarpMode = WarpMode.Subspace`, `ConsoleIdentifier = "x"`; assert the deserialized message has `AllowQuickLoad == true`, `QuickLoadMaxRewindMinutes == 45`, and still `WarpMode.Subspace`.
- [ ] **Step 2:** Run `dotnet test LmpCommonTest/LmpCommonTest.csproj --filter SettingsReply_RoundTripsQuickLoadSettings`. Expected: compile failure, fields do not exist.
- [ ] **Step 3:** Add the two fields to `SettingsReplyMsgData`. Write and read them directly after `WarpMode` in both methods, and add `sizeof(bool) + sizeof(int)` to the size.
- [ ] **Step 4:** Run the test. Expected: PASS. Run the whole `LmpCommonTest` project. Expected: all pass.
- [ ] **Step 5:** Add the two server settings, fill them in `SettingsMsgReader` outside the `GameDifficulty.Custom` block, expose them in `ServerWarpSettings`, and copy them into `ServerSettings` in the client handler.
- [ ] **Step 6:** `dotnet test ServerTest/ServerTest.csproj` passes; `Server` and `LmpClient` build.
- [ ] **Step 7: Commit** `feat: add AllowQuickLoad and QuickLoadMaxRewindMinutes server settings`

### Task 2: Decision logic without KSP types

**Files:**
- Create: `LmpCommon/QuickLoad/QuickLoadFamily.cs`
- Create: `LmpCommon/QuickLoad/QuickLoadCheck.cs`
- Test: `LmpCommonTest/QuickLoadTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace LmpCommon.QuickLoad
{
    public enum FamilyRelation { Outside, Member, MemberWithOutsideParts }

    public class QuickLoadFamily
    {
        public QuickLoadFamily(IEnumerable<uint> partFlightIds, IEnumerable<string> crewNames);
        // evaKerbalName is null unless the vessel is a kerbal on EVA
        public FamilyRelation Classify(IEnumerable<uint> vesselPartFlightIds, string evaKerbalName);
    }

    public enum QuickLoadRefusal
    {
        None, FeatureDisabled, NoSnapshot, Spectating, WarpingOrWaitingSubspace,
        LockedByOtherPlayer, DockedWithOutsideVessel, CrewChanged, SnapshotTooOld
    }

    public struct QuickLoadState
    {
        public bool FeatureEnabled;            // AllowQuickLoad && WarpMode == Subspace
        public bool HasSnapshot;
        public bool Spectating;
        public bool Warping;
        public bool WaitingSubspaceId;
        public bool FamilyVesselLockedByOther;
        public bool DockedWithOutsider;
        public string[] SnapshotCrew;
        public string[] CrewAboardFamily;      // crew currently on vessels inside the family
        public string[] CrewAboardOtherVessels;// crew currently on any vessel outside the family
        public double SnapshotTime;
        public double CurrentTime;
        public int MaxRewindMinutes;
    }

    public static class QuickLoadCheck
    {
        public static QuickLoadRefusal Decide(QuickLoadState state);
    }
}
```

`Classify`: an EVA kerbal is `Member` if its name is in the crew names, else `Outside`. Otherwise: no part in the set → `Outside`; all parts in the set → `Member`; some in, some not → `MemberWithOutsideParts`. An empty part list is `Outside`.

`Decide` checks in the enum's order and returns the first that applies. `CrewChanged`: a name in `SnapshotCrew` is in `CrewAboardOtherVessels`, or a name in `CrewAboardFamily` is not in `SnapshotCrew`. `SnapshotTooOld`: `MaxRewindMinutes > 0 && CurrentTime - SnapshotTime > MaxRewindMinutes * 60`. Null arrays count as empty.

- [ ] **Step 1: Write failing tests** in `QuickLoadTests.cs`. A helper `Ok()` returns a state that yields `None` (enabled, snapshot, crew `{"Jeb"}` aboard family, times 1000/1100, limit 60).
  - `Family_AllPartsKnown_IsMember`; `Family_SubsetOfParts_IsMember` (a dropped stage); `Family_NoKnownParts_IsOutside`; `Family_EmptyPartList_IsOutside`
  - `Family_KnownAndUnknownParts_IsMemberWithOutsideParts`
  - `Family_EvaKerbalFromCrew_IsMember`; `Family_EvaKerbalNotInCrew_IsOutside`
  - `Decide_Ok_ReturnsNone`
  - one test per refusal, flipping a single field of `Ok()` and asserting that refusal, including `Refuses_WhileWaitingForSubspaceId` → `WarpingOrWaitingSubspace`
  - `CrewChanged_WhenSnapshotCrewIsOnOtherVessel`; `CrewChanged_WhenStrangerIsAboardFamily`; `CrewDeadOrMissing_IsAllowed` (`CrewAboardFamily` empty → `None`)
  - `ZeroOrNegativeLimit_MeansNoLimit` (limits `0` and `-5`, times 0 / 1e9 → `None`)
  - `ExactlyAtLimit_IsAllowed` (difference 3600 with limit 60 → `None`); `OneSecondOver_IsRefused` → `SnapshotTooOld`
  - `Order_FeatureDisabledWinsOverNoSnapshot`
- [ ] **Step 2:** `dotnet test LmpCommonTest/LmpCommonTest.csproj --filter QuickLoadTests`. Expected: compile failure.
- [ ] **Step 3:** Implement both classes.
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5: Commit** `feat: quickload family classification and refusal rules`

### Task 3: Spike in the game (person at the keyboard, throwaway code)

Answers four questions the design leaves open. Use a scratch branch; nothing from it is kept. Write the answers at the bottom of this plan under "Spike results" and commit only that.

- [ ] **Q1.** With `Flight.CanQuickLoad == false` and connected, does pressing F9 reach `QuickSaveLoad.quickLoad` (does the `CannotLoadGames` message appear)? Also confirm the exact name of the stock quicksave method that F5 calls and how to tell that stock accepted the save.
- [ ] **Q2.** In flight, call `TimeSyncSystem.Singleton.SetGameTime(now - 300)` then set `WarpSystem.Singleton.CurrentSubspace = -1`. Does the client get a new subspace 5 minutes behind without errors, and do other loaded vessels and part modules behave?
- [ ] **Q3.** Take a config node of the active vessel, give it a new `pid`, kill the old vessel without `SwitchVesselIfKillingActiveVessel`, load the node with `VesselLoader.LoadVessel(proto, true)` and `FlightGlobals.ForceSetActiveVessel`. Does it work in place, or is a flight scene reload needed? Do `persistentId` / `flightID` values have to change too?
- [ ] **Q4.** After Q2+Q3 with a second client connected: does the second client see exactly one vessel, and do other players' vessels look right on the rewound client, or must position/flight-state queues be cleared?

**Stop here if Q2 or Q3 has no workable answer** and take the result back to Malte; the design depends on both.

### Task 4: Snapshot on F5

**Files:**
- Create: `LmpClient/Systems/QuickLoad/QuickLoadSystem.cs`, `LmpClient/Systems/QuickLoad/QuickLoadSnapshot.cs`
- Create: `LmpClient/Harmony/QuickSaveLoad_QuickSave.cs` (postfix; method name from Spike Q1)
- Modify: `LmpClient/Localization/Structures/ScreenText.cs` and every `LmpClient/Localization/XML/*/ScreenText.xml`

**Interfaces:**
- Consumes: Task 1 client settings; `QuickLoadFamily` from Task 2.
- Produces:

```csharp
public class QuickLoadSnapshot
{
    public double GameTime;
    public ConfigNode VesselNode;
    public Guid VesselId;
    public string[] Crew;
    public QuickLoadFamily Family;
}

public class QuickLoadSystem : System<QuickLoadSystem>
{
    public QuickLoadSnapshot Snapshot { get; private set; }
    public bool FeatureEnabled { get; } // AllowQuickLoad && WarpMode == Subspace
    public void TakeSnapshot();         // called by the F5 postfix
    public void ClearSnapshot();
}
```

- New `ScreenText` properties with these English defaults: `QuickSaved` = "Quicksaved (multiplayer)", `QuickLoaded` = "Quickloaded: you are now in an earlier subspace", `QuickSaveNotOwnVessel` = "Cannot quicksave: you are not controlling this vessel", `QuickSaveEva` = "Cannot quicksave a kerbal on EVA", `QuickLoadNoSnapshot` = "No multiplayer quicksave to load", `QuickLoadSpectating` = "Cannot quickload while spectating", `QuickLoadWarping` = "Cannot quickload while warping", `QuickLoadLocked` = "Cannot quickload: another player controls part of your vessel", `QuickLoadDocked` = "Cannot quickload: your vessel docked with another vessel since the quicksave", `QuickLoadCrewChanged` = "Cannot quickload: crew moved to or from another vessel since the quicksave", `QuickLoadTooOld` = "Cannot quickload: the quicksave is too old for this server". Non-English XML files get the English text.

- [ ] **Step 1:** Implement `TakeSnapshot`. It does nothing when `FeatureEnabled` is false. It refuses with `QuickSaveNotOwnVessel` when spectating or when the player does not hold the control lock of the active vessel (`LockSystem.LockQuery.ControlLockBelongsToPlayer`), and with `QuickSaveEva` when `FlightGlobals.ActiveVessel.isEVA`. Otherwise it stores a fresh `BackupVessel()` saved to a `ConfigNode`, the game time, vessel id, crew names and a new `QuickLoadFamily` built from the flight ids of the vessel's parts and the crew names, and shows `QuickSaved`.
- [ ] **Step 2:** Add the Harmony postfix. It calls `TakeSnapshot` only when connected (`MainSystem.NetworkState >= ClientState.Connected`) and stock accepted the save (per Spike Q1).
- [ ] **Step 3:** Add the texts.
- [ ] **Step 4:** Build the client. Manual check with the setting on: F5 in orbit shows `QuickSaved`; F5 on EVA shows `QuickSaveEva`; with the setting off F5 shows nothing new.
- [ ] **Step 5: Commit** `feat: take a multiplayer quicksave snapshot on F5`

### Task 5: Family lookup and snapshot lifetime

**Files:**
- Create: `LmpClient/Systems/QuickLoad/QuickLoadEvents.cs`
- Modify: `LmpClient/Systems/QuickLoad/QuickLoadSystem.cs` (`OnEnabled` / `OnDisabled`)

**Interfaces:**
- Consumes: `QuickLoadFamily.Classify` (Task 2); `RemoveEvent.onLmpRecoveredVessel`, `RemoveEvent.onLmpTerminatedVessel` (both `ProtoVessel`).
- Produces: `public FamilyRelation RelationOf(Vessel vessel)` on `QuickLoadSystem` (returns `Outside` when there is no snapshot). It reads part flight ids from `vessel.parts` when loaded and from `vessel.protoVessel.protoPartSnapshots` when not, and passes the kerbal's name when `vessel.isEVA`.

- [ ] **Step 1:** Implement `RelationOf`.
- [ ] **Step 2:** Subscribe to the two remove events in `OnEnabled`, unsubscribe in `OnDisabled`, mirroring `RevertSystem`. If the recovered or terminated proto vessel classifies as anything but `Outside`, call `ClearSnapshot()`.
- [ ] **Step 3:** `OnDisabled` calls `ClearSnapshot()`.
- [ ] **Step 4:** Build. Manual check with a temporary debug log listing family vessels (remove before commit): F5, stage twice → three family vessels; dock with an unrelated craft → `MemberWithOutsideParts`; go on EVA → the kerbal is a member; disconnect and reconnect → no snapshot.
- [ ] **Step 5: Commit** `feat: classify vessels against the quicksaved vessel`

### Task 6: F9 — rewind and restore

**Files:**
- Modify: `LmpClient/Systems/QuickLoad/QuickLoadSystem.cs`
- Modify: `LmpClient/Harmony/QuickSaveLoad_QuickLoad.cs`
- Modify (only if Spike Q1 says stock never calls `quickLoad`): `QuickLoadSystem` gets an `Update` routine that reads `GameSettings.QUICKLOAD` itself in the flight scene; `Flight.CanQuickLoad` stays `false`.
- Modify (only if needed for the swap): `LmpClient/Systems/VesselRemoveSys/VesselRemoveSystem.cs` — add an overload or parameter to `KillVessel` that skips `SwitchVesselIfKillingActiveVessel`.

**Interfaces:**
- Consumes: `QuickLoadCheck.Decide`, `QuickLoadState`, `QuickLoadRefusal` (Task 2); `QuickLoadSnapshot` (Task 4); `TimeSyncSystem.Singleton.SetGameTime(double)`; `WarpSystem.Singleton.CurrentSubspace`, `.CurrentlyWarping`, `.WaitingSubspaceIdFromServer`; `VesselRemoveSystem.Singleton.KillVessel`, `.MessageSender.SendVesselRemove(Guid, bool, string)`; `VesselLoader.LoadVessel(ProtoVessel, bool)`; `VesselProtoSystem.Singleton.MessageSender.SendVesselMessage(Vessel, bool, string)`; `KerbalSystem.Singleton.MessageSender.SendKerbal(ProtoCrewMember)`.
- Produces: `public void TryQuickLoad()` on `QuickLoadSystem`.

- [ ] **Step 1:** Add `private QuickLoadState BuildState()`. It fills every field from live data and must work when `FlightGlobals.ActiveVessel` is null. Classify every vessel in `FlightGlobals.Vessels` with `RelationOf`. `FamilyVesselLockedByOther`: for any family vessel, `ControlLockExists(id) && !ControlLockBelongsToPlayer(id, playerName)`. `DockedWithOutsider`: any vessel is `MemberWithOutsideParts`. Crew lists are split by family / not family.
- [ ] **Step 2:** Change the prefix in `QuickSaveLoad_QuickLoad.cs`: not connected → `return true` as today; `QuickLoadSystem.Singleton.FeatureEnabled` false → today's `CannotLoadGames` message; otherwise call `TryQuickLoad()`. Always `return false` while connected.
- [ ] **Step 3:** Implement `TryQuickLoad`. Call `QuickLoadCheck.Decide(BuildState())`; on a refusal show the matching text (`FeatureDisabled` → `CannotLoadGames`) and return. Otherwise, in this order:
  1. `SetGameTime(Snapshot.GameTime)`, then `CurrentSubspace = -1`. Do not call `RequestNewSubspace`; `CheckWarpStopped` sends the request.
  2. For every family vessel that still exists: send the remove with `keepVesselInRemoveList: true`, reason `"Quickload"`, and kill it locally without the active-vessel switch.
  3. Copy `Snapshot.VesselNode`, give it a fresh vessel id (and whatever else Spike Q3 found necessary), build the `ProtoVessel`, `LoadVessel(proto, true)`, make it active, and `SendVesselMessage(vessel, true, "Quickload")`. Apply the in-place or scene-reload variant chosen in Spike Q3, and the queue clearing from Spike Q4 if needed.
  4. For each name in `Snapshot.Crew` whose roster entry is dead or missing: set `rosterStatus = Assigned` and `SendKerbal`.
  5. `Snapshot.VesselId = newId`; rebuild `Snapshot.Family` from the restored vessel's parts if their flight ids changed; show `QuickLoaded`.
- [ ] **Step 4:** Build. Run checks 1, 2 and 6 from Task 7.
- [ ] **Step 5: Commit** `feat: quickload restores the vessel in an earlier subspace`

### Task 7: Two-client test pass and docs

**Files:**
- Modify: `LMP Readme.txt` or the server settings documentation, wherever `WarpMode` is described for operators (search for it; if nowhere, add nothing).

- [ ] **Step 1:** With two clients on a local server and `AllowQuickLoad` on, run every check from the spec's "Testing" section (1–7). Record pass/fail per check under "Test results" at the bottom of this plan. Extra checks from Review Focus: F9 with the crash dialog open and no active vessel; F9 pressed twice quickly; F5 on EVA; reconnect then F9.
- [ ] **Step 2:** Fix what fails; a fix that changes the design goes back to Malte first.
- [ ] **Step 3:** Document the two settings for operators, including that the limit is enforced by the client.
- [ ] **Step 4: Commit** `docs: describe multiplayer quickload settings`

---

## Spike results

**Q1 — answered without the game, by reading the stock `QuickSaveLoad` class (KSP build 03190).**

- With `Flight.CanQuickLoad == false`, stock shows its own "quickload disabled" message on F9 and never calls `quickLoad`. The existing LMP prefix therefore never fires in flight. `QuickLoadSystem` reads `GameSettings.QUICKLOAD` itself, and holds an input lock on `ControlTypes.QUICKLOAD` while the feature is enabled so stock stays silent. `CanQuickLoad` stays `false`.
- Stock's F5 path is `quickSave(bool saveAs)` → `doSave(string filename, string screenMsg)`. `doSave` only runs when stock accepted the save, and it fires `GameEvents.onGameAboutToQuicksave` first. The snapshot is taken from that event; no Harmony patch is needed for F5.

**Q2–Q4 — not yet answered.** They are checked with the real implementation in the first in-game session instead of with throwaway code (see ledger ruling).

## Test results

(filled in by Task 7)
