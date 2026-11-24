# Multiplayer quicksave / quickload (personal rewind) — design

Date: 2026-11-24
Status: draft, awaiting approval

## Intent

A player wants to press F5 before a maneuver that is easy to fail (landing,
orbit change, docking) and press F9 after failing it to try the same maneuver
again. Today LMP blocks F9 outright while connected.

"Try again" means the same starting conditions: the vessel as it was, and the
world where it was (planet rotation, moons, an uncontrolled docking target).
So the player's clock has to go back too, not only the vessel.

## Goals

- F5 in flight stores a snapshot of the vessel the player is flying, together
  with the game time.
- F9 in flight puts that vessel back and moves the player to the saved game
  time, in a subspace of their own.
- Other players keep playing undisturbed. They see the old vessel disappear
  and the restored one appear.
- Server operators can switch the feature on or off, on any difficulty.

## Non-goals (v1)

- Restoring anything the player does not fly: other players' vessels, an
  unowned docking target that got damaged, bases nearby.
- Rolling back shared career data (funds, science, reputation, contracts,
  tech). Whatever changed between F5 and F9 stays changed.
- Quickload from the KSC or tracking station. Those stay blocked as today.
- Named saves (Alt+F5 / Alt+F9), the pause-menu load button, several slots,
  or saves that survive a disconnect. One in-memory slot per session.
- Warp modes other than `Subspace`.
- Server-side policing of rewinds. See "Server".

## Anti-goals

- A quickload must never change the clock or the vessels of another player.
- A quickload must never duplicate value: no restoring a vessel that was
  recovered for funds, no second copy of a vessel, part or kerbal that still
  exists elsewhere.
- No change to behaviour on servers that leave the setting off, including
  how ordinary warp-stops are handled.

## How the relevant parts work today

- F9 is blocked by two Harmony prefixes, `LmpClient/Harmony/QuickSaveLoad_QuickLoad.cs`
  (flight) and `LmpClient/Harmony/KSCPauseMenu_QuickLoad.cs` (KSC).
  `SettingsMessageHandler.cs:111` also forces `Flight.CanQuickLoad = false`.
  F5 is not patched; stock writes `quicksave.sfs` locally.
- A subspace is one number: seconds of offset from the server clock
  (`Server/System/WarpSystem.cs`, `LmpClient/Systems/Warp/WarpSystem.cs`).
- When the client's subspace is `-1`, it is not waiting for an id, and the
  warp rate is 1, the 100 ms routine `WarpSystem.CheckWarpStopped` calls the
  private `RequestNewSubspace`. That sends `WarpNewSubspaceMsgData` with
  `ServerTimeDifference = UniversalTime - ServerClockSec`
  (`WarpMessageSender.SendNewSubspace`). The server replies with a new id
  (`WarpSystemReceiver.HandleNewSubspace`) and does not validate the offset,
  so an offset lower than the current one is accepted today.
- While subspace is `-1` or `WaitingSubspaceIdFromServer` is set,
  `TimeSyncSystem.CheckGameTime` does not correct the clock.
- Being behind other players is normal. Vessel messages carry `GameTime` and
  a client holds them until its own time reaches it
  (`VesselProtoSystem.cs:233`); position updates from other subspaces are
  projected (`VesselPositionUpdate.cs`). The status window only offers
  syncing to subspaces in the future.
- Revert to launch (`VesselRemoveEvents.OnRevertToLaunch`) sends remove
  messages for the active vessel (not kill-listed) and for every vessel
  sharing its `rootPart.missionID` (kill-listed). It removes nothing locally;
  KSP's own scene reload does that. Its helper `RemoveOldVesselAndItsDebris`
  is private and tied to `FlightGlobals.ActiveVessel`.
- `VesselRemoveSystem.KillVessel` removes a vessel locally. If it is the
  active vessel, `SwitchVesselIfKillingActiveVessel` switches to the nearest
  controllable vessel or, if there is none, loads the space centre scene.
- The server keeps a permanent kill list (`VesselContext.RemovedVessels`);
  `HandleVesselProto` drops any proto whose id is on it. When the player's
  own active vessel is destroyed it is removed without kill-listing; other
  dying vessels (debris) are kill-listed (`VesselRemoveEvents.cs:32-44`).
- The server rejects a vessel removal only when a control lock exists and
  belongs to someone else (`VesselMsgReader.cs:90`).
- Gameplay settings such as `CanRevert` are only sent when the difficulty is
  `Custom` (`SettingsMsgReader.cs:41`). `WarpMode` and the general settings
  are always sent.

## Approaches considered

**A. Personal rewind (chosen).** Restore the vessel and move the player into
a new subspace at the saved time. The world is exactly as it was at F5. Cost:
the player is behind the others by the length of the failed attempt and
catches up with the existing sync button.

**B. Restore in place.** Put the vessel back on its saved orbit at the
current time. Simpler and everyone stays in step, but the planet has rotated,
the vessel is further along its orbit, and a save made during descent cannot
be restored safely. Rejected: it undoes damage but is not the same attempt.

**C. B with the orbit shifted to the same point.** Rejected: an uncontrolled
docking target has moved on, so docking retries break.

Chosen by Malte on 2026-11-24: A.

## Design

### New client system: `QuickLoadSystem` (`LmpClient/Systems/QuickLoad/`)

Holds one snapshot and performs save and load. Enabled only when the server
setting is on and warp mode is `Subspace`.

Snapshot contents:

- game time (`Planetarium.GetUniversalTime()`) at F5
- the active vessel as a config node (from a fresh `BackupVessel()`)
- the vessel's id
- names of the crew on board
- the flight ids of all parts of the vessel at F5 (the **part set**)

The part set defines the vessel's **family** at any later moment, with no
event tracking: a vessel is in the family if it contains at least one part
from the part set, or if it is a kerbal on EVA whose name is in the saved
crew. Part flight ids survive staging, undocking and breaking apart, so the
family is the vessel plus everything that came off it since F5. This covers a
module that was docked on before F5 and undocked after, and it never touches
unrelated craft. A family vessel that also contains parts from outside the
part set has docked with an outside vessel.

The snapshot is cleared on disconnect, and when a family vessel is recovered
or terminated.

### F5

Harmony postfix on `QuickSaveLoad.quickSave`. Stock runs unchanged (it still
writes its local file, which nothing reads). If stock considered the game
clear to save, and the player is not spectating and holds the control lock of
the active vessel, take the snapshot and show "Quicksaved (multiplayer)".
Otherwise show why not.

### F9

The existing prefix in `QuickSaveLoad_QuickLoad.cs` calls
`QuickLoadSystem.TryQuickLoad()` instead of only showing the refusal. Stock
quickload never runs while connected.

Refuse, with a screen message naming the reason, when:

- the feature is off on the server, or warp mode is not `Subspace`
- there is no snapshot
- the player is spectating, currently warping, or waiting for a subspace id
- another player holds the control lock on any family vessel
- a family vessel has docked or coupled with an outside vessel since F5
- the crew does not add up: a kerbal from the snapshot is now aboard a vessel
  outside the family, or a kerbal who is not in the snapshot is
  aboard a vessel inside it
- the snapshot is older than the server's limit (see settings)

Otherwise:

1. **Rewind the clock.** Set the game time to the snapshot time with
   `TimeSyncSystem.SetGameTime`, then set `CurrentSubspace = -1`. The
   existing `CheckWarpStopped` routine then requests the new subspace by
   itself, with an offset computed from the rewound clock. No second request
   is sent. The client ends up alone in a subspace at the snapshot time.
   The server accepts this today and is not changed, so there is no
   rejection case to handle.
2. **Swap the vessels.** Remove every family vessel that still
   exists, locally and on the server, all kill-listed. Load the snapshot
   under a fresh vessel id through `VesselLoader.LoadVessel`, make it the
   active vessel, and send it with
   `VesselProtoSystem.MessageSender.SendVesselMessage`. The lock system
   acquires the control lock on the vessel change as usual.
   The removal must not go through `SwitchVesselIfKillingActiveVessel`: the
   player must not be switched to a neighbour (which would take its lock) or
   thrown back to the space centre.
3. **Restore the crew.** Any crew member in the snapshot who is now dead or
   missing is set back to assigned and sent through `KerbalSystem` so the
   server roster matches.
4. The snapshot stays, so F9 can be pressed again. Its vessel id is
   updated to the fresh id; the part set stays valid if part flight ids are
   kept, otherwise it is rebuilt from the restored vessel.

Other clients need no new code: they receive ordinary remove and proto
messages. The proto carries a `GameTime` in their past, so they apply it at
once and see the player as someone in an earlier subspace.

### Server

Settings only. No change to subspace handling, vessel storage, the kill list
or `Subspace.txt`.

- `AllowQuickLoad` (bool) and `QuickLoadMaxRewindMinutes` (int, 0 = no limit)
  in `WarpSettingsDefinition`, sent unconditionally in `SetingsReplyMsgData`
  next to `WarpMode`, so they work on every difficulty.
- The client enforces both. The server does not police rewinds: it already
  accepts any offset from any client, and a rule that only judged requests a
  client labels as rewinds would stop nobody who modifies their client, while
  a rule on all lower offsets would hit ordinary warp-stops from a lagging
  client.
- `WarpAllotmentTracker.RecordWarp` already ignores negative deltas.

Adding fields to the settings message changes the wire format, so client and
server must be the same version, as with any LMP protocol change.

### Texts

New `ScreenText` entries (English defaults, plus the XML language files):
quicksaved, quickloaded, and one per refusal reason.

## Calls made by the designer (not decided by Malte)

- `AllowQuickLoad` defaults to **false**, so upstream behaviour is unchanged
  unless an operator opts in.
- `QuickLoadMaxRewindMinutes` defaults to **60**.
- Limits are enforced by the client only (reasons under "Server").
- The restored vessel always gets a fresh id. It keeps one code path, and
  late updates still in flight for the old vessel cannot land on the
  restored one.
- What F9 removes is decided by part flight ids saved at F5, not by
  `missionID` and not by following events.
- F9 is refused after docking with an outside vessel, and when crew moved in
  or out of the vessel's family, rather than trying to untangle those cases.
- One in-memory slot; stock's own quicksave file is ignored.
- Career data is left alone. Known consequence: science transmitted or funds
  earned between F5 and F9 are kept.

## Left to the builder

- **Check first, against the game:** whether stock calls
  `QuickSaveLoad.quickLoad` at all while `Flight.CanQuickLoad` is false. If
  it does not, `QuickLoadSystem` reads the quickload key itself and
  `CanQuickLoad` stays false, so Alt+F9 and the pause-menu load button remain
  blocked.
- Whether the vessel can be swapped in place or the flight scene must be
  reloaded for a clean physics state. Start with in-place; fall back to a
  scene reload if the restored vessel misbehaves. Either way the old and the
  restored vessel must not be loaded at the same spot for a physics frame.
- How to give the config node a fresh id safely: vessel `pid`, and whether
  vessel/part `persistentId` and part `flightID` values must change too.
- Whether setting the clock backwards in flight upsets other loaded vessels
  or part modules that track a last-update time, and what to reset if so.
- Whether the position/flight-state queues for other players' vessels need
  clearing after the rewind.

## Testing

- `LmpCommonTest` / `ServerTest`: the new settings round-trip through
  `SetingsReplyMsgData` and are sent on a non-custom difficulty.
- Keep the refusal checks and the family classification in plain classes
  without KSP types where possible, and unit test those.
- The rest of the client cannot be unit tested without KSP. Manual checks
  with two clients on a local server:
  1. Orbit: F5, burn, F9. Orbit, fuel and game time match the save; the
     other client sees one vessel, not two.
  2. Landing: F5 in low orbit, stage, crash, F9. Vessel and crew are back;
     the stage dropped after F5 and the wreckage are gone; debris dropped
     before F5 is still there.
  3. Docking: F5 at 100 m from an uncontrolled target, miss, F9. Target is at
     the same relative position as at F5 and undamaged vessels nearby are
     untouched.
  4. After F9 the status window shows the player in an earlier subspace and
     the sync button brings them back to the others.
  5. Refusals: no snapshot, spectating, vessel recovered, docked with the
     target, kerbal moved to another vessel, setting off, limit exceeded.
  6. F9 twice in a row.
  7. Setting off: F9 shows the old refusal and warp-stops behave as before.
