using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Localization;
using LmpClient.Systems.KerbalSys;
using LmpClient.Systems.Lock;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.VesselRemoveSys;
using LmpClient.Systems.Warp;
using LmpClient.VesselUtilities;
using LmpCommon.Enums;
using LmpCommon.QuickLoad;
using LmpCommon.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LmpClient.Systems.QuickLoad
{
    /// <summary>
    /// Multiplayer quicksave (F5) and quickload (F9).
    /// F5 remembers the vessel you are flying. F9 puts that vessel back and moves you, and only you,
    /// into a new subspace at the time of the quicksave. Other players and their vessels are never touched.
    /// </summary>
    public class QuickLoadSystem : System<QuickLoadSystem>
    {
        #region Fields & properties

        private const string QuickLoadInputLock = "LMP_QuickLoad";
        private const float HoldSeconds = 1f;

        private static QuickLoadEvents QuickLoadEvents { get; } = new QuickLoadEvents();

        private float _keyDownTime = -1;
        private bool _quickLoadInProgress;

        public QuickLoadSnapshot Snapshot { get; private set; }

        /// <summary>
        /// The server allows quickloading and uses subspace warping
        /// </summary>
        public bool FeatureEnabled => SettingsSystem.ServerSettings.AllowQuickLoad && SettingsSystem.ServerSettings.WarpMode == WarpMode.Subspace;

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(QuickLoadSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();
            if (!FeatureEnabled) return;

            GameEvents.onGameAboutToQuicksave.Add(QuickLoadEvents.OnGameAboutToQuicksave);
            RemoveEvent.onLmpRecoveredVessel.Add(QuickLoadEvents.OnVesselRecoveredOrTerminated);
            RemoveEvent.onLmpTerminatedVessel.Add(QuickLoadEvents.OnVesselRecoveredOrTerminated);
            PartEvent.onPartCoupling.Add(QuickLoadEvents.OnPartCoupling);
            FlightDriverEvent.onFlightStarted.Add(QuickLoadEvents.OnFlightStarted);

            //Stock never calls its quickload while Flight.CanQuickLoad is false, it only shows a "disabled" message.
            //Lock the stock key handling so that message does not show and read the key ourselves.
            InputLockManager.SetControlLock(ControlTypes.QUICKLOAD, QuickLoadInputLock);
            SetupRoutine(new RoutineDefinition(0, RoutineExecution.Update, CheckQuickLoadKey));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            GameEvents.onGameAboutToQuicksave.Remove(QuickLoadEvents.OnGameAboutToQuicksave);
            RemoveEvent.onLmpRecoveredVessel.Remove(QuickLoadEvents.OnVesselRecoveredOrTerminated);
            RemoveEvent.onLmpTerminatedVessel.Remove(QuickLoadEvents.OnVesselRecoveredOrTerminated);
            PartEvent.onPartCoupling.Remove(QuickLoadEvents.OnPartCoupling);
            FlightDriverEvent.onFlightStarted.Remove(QuickLoadEvents.OnFlightStarted);
            FinishQuickLoad(false);
            InputLockManager.RemoveControlLock(QuickLoadInputLock);
            _keyDownTime = -1;
            ClearSnapshot();
        }

        #endregion

        #region Update methods

        /// <summary>
        /// Like stock, quickload needs the key to be held for a moment so it's not triggered by accident
        /// </summary>
        private void CheckQuickLoadKey()
        {
            //Safety net: the reload of a quickload ended up in another scene, do not hold the subspace request forever
            if (_quickLoadInProgress && HighLogic.LoadedSceneIsGame && !HighLogic.LoadedSceneIsFlight)
                FinishQuickLoad(false);

            if (!HighLogic.LoadedSceneIsFlight || GameSettings.MODIFIER_KEY.GetKey())
            {
                _keyDownTime = -1;
                return;
            }

            if (GameSettings.QUICKLOAD.GetKeyDown())
                _keyDownTime = Time.realtimeSinceStartup;
            else if (GameSettings.QUICKLOAD.GetKeyUp())
                _keyDownTime = -1;

            if (_keyDownTime >= 0 && GameSettings.QUICKLOAD.GetKey() && Time.realtimeSinceStartup - _keyDownTime >= HoldSeconds)
            {
                _keyDownTime = -1;
                TryQuickLoad();
            }
        }

        #endregion

        #region Public methods

        public void ClearSnapshot()
        {
            Snapshot = null;
        }

        /// <summary>
        /// Remembers the vessel we are flying. Called when stock accepts a quicksave.
        /// </summary>
        public void TakeSnapshot()
        {
            if (!Enabled || !FeatureEnabled || !HighLogic.LoadedSceneIsFlight) return;

            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.state == Vessel.State.DEAD) return;

            if (VesselCommon.IsSpectating || !LockSystem.LockQuery.ControlLockBelongsToPlayer(vessel.id, SettingsSystem.CurrentSettings.PlayerName))
            {
                Show(LocalizationContainer.ScreenText.QuickSaveNotOwnVessel);
                return;
            }

            if (vessel.isEVA)
            {
                Show(LocalizationContainer.ScreenText.QuickSaveEva);
                return;
            }

            try
            {
                var node = new ConfigNode();
                vessel.BackupVessel().Save(node);

                var crew = vessel.GetVesselCrew().Select(c => c.name).ToArray();
                Snapshot = new QuickLoadSnapshot
                {
                    GameTime = Planetarium.GetUniversalTime(),
                    VesselNode = node,
                    VesselId = vessel.id,
                    Crew = crew,
                    Family = new QuickLoadFamily(vessel.parts.Select(p => p.flightID), crew)
                };

                LunaLog.Log($"[LMP]: Quicksaved vessel {vessel.id} ({vessel.vesselName}) at {Snapshot.GameTime}");
                Show(LocalizationContainer.ScreenText.QuickSaved);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Error taking the quicksave snapshot: {e}");
            }
        }

        /// <summary>
        /// Tells how the given vessel relates to the vessel that was quicksaved
        /// </summary>
        public FamilyRelation RelationOf(Vessel vessel)
        {
            if (Snapshot == null || vessel == null) return FamilyRelation.Outside;

            if (vessel.isEVA)
                return Snapshot.Family.Classify(null, CrewNamesOf(vessel).FirstOrDefault() ?? vessel.vesselName);

            if (vessel.loaded && vessel.parts != null)
                return Snapshot.Family.Classify(vessel.parts.Where(p => p != null).Select(p => p.flightID), null);

            return RelationOf(vessel.protoVessel);
        }

        /// <summary>
        /// Tells how the given vessel relates to the vessel that was quicksaved
        /// </summary>
        public FamilyRelation RelationOf(ProtoVessel protoVessel)
        {
            if (Snapshot == null || protoVessel == null) return FamilyRelation.Outside;

            if (protoVessel.vesselType == VesselType.EVA)
                return Snapshot.Family.Classify(null, protoVessel.GetVesselCrew().FirstOrDefault()?.name ?? protoVessel.vesselName);

            return Snapshot.Family.Classify(protoVessel.protoPartSnapshots.Select(p => p.flightID), null);
        }

        /// <summary>
        /// Puts the quicksaved vessel back and moves us to the time of the quicksave, in a subspace of our own
        /// </summary>
        public void TryQuickLoad()
        {
            if (!Enabled || !HighLogic.LoadedSceneIsFlight) return;

            try
            {
                var vessels = FlightGlobals.Vessels.Where(v => v != null && v.state != Vessel.State.DEAD).ToArray();
                var familyVessels = vessels.Where(v => RelationOf(v) != FamilyRelation.Outside).ToArray();

                var refusal = QuickLoadCheck.Decide(BuildState(vessels, familyVessels));
                if (refusal != QuickLoadRefusal.None)
                {
                    Show(RefusalText(refusal));
                    return;
                }

                DoQuickLoad(familyVessels);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Error while quickloading: {e}");
                Show(LocalizationContainer.ScreenText.QuickLoadFailed);
            }
        }

        /// <summary>
        /// Called when the flight scene reloaded by a quickload has started (or when that reload ended up elsewhere).
        /// From here on the warp system requests our new subspace, using the clock that the reload has rewound.
        /// </summary>
        public void FinishQuickLoad(bool flightStarted)
        {
            if (!_quickLoadInProgress) return;

            _quickLoadInProgress = false;
            WarpSystem.Singleton.HoldSubspaceRequest = false;

            if (flightStarted)
                Show(LocalizationContainer.ScreenText.QuickLoaded);
        }

        /// <summary>
        /// Another player removed a vessel. If it was recovered or terminated and it belongs to our quicksaved vessel,
        /// the quicksave must not bring it back.
        /// </summary>
        public void OnRemoteVesselRemove(Guid vesselId, string reason)
        {
            if (Snapshot == null || !QuickLoadCheck.IsRecoveryReason(reason)) return;

            if (RelationOf(FlightGlobals.FindVessel(vesselId)) != FamilyRelation.Outside)
                ClearSnapshot();
        }

        #endregion

        #region Private methods

        private void DoQuickLoad(Vessel[] familyVessels)
        {
            var snapshot = Snapshot;
            LunaLog.Log($"[LMP]: Quickloading vessel {snapshot.VesselId}. Going from {Planetarium.GetUniversalTime()} back to {snapshot.GameTime}");

            //--- First everything that can fail. Nothing below, up to the next marker, is seen by the server or other players ---

            //Take the current state of the game
            var game = HighLogic.CurrentGame.Updated();

            if (snapshot.Crew.Any(c => game.CrewRoster[c] == null))
            {
                LunaLog.LogWarning("[LMP]: Quickload refused: a kerbal of the quicksaved vessel is not in the roster anymore");
                Show(LocalizationContainer.ScreenText.QuickLoadCrewChanged);
                return;
            }

            //Bring back the crew that died or went missing since the quicksave
            var revivedCrew = snapshot.Crew.Select(c => game.CrewRoster[c])
                .Where(c => c.rosterStatus != ProtoCrewMember.RosterStatus.Assigned)
                .Select(c => new KeyValuePair<ProtoCrewMember, ProtoCrewMember.RosterStatus>(c, c.rosterStatus)).ToArray();

            foreach (var crew in revivedCrew)
                crew.Key.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;

            //The quicksaved vessel comes back under a NEW vessel id. The old id may be in the server kill list, and late
            //updates that are still travelling for the old vessel must not be applied to the restored one.
            var newVesselId = Guid.NewGuid();
            ProtoVessel protoVessel;
            try
            {
                var node = snapshot.VesselNode.CreateCopy();
                node.SetValue("pid", newVesselId.ToString("N"), true);
                protoVessel = new ProtoVessel(node, game);
            }
            catch (Exception)
            {
                foreach (var crew in revivedCrew)
                    crew.Key.rosterStatus = crew.Value;
                throw;
            }

            //--- From here on there is no way back ---

            //Leave our subspace but hold the request for a new one until the flight scene has reloaded.
            //The reload sets the clock to the quicksave time and only then we must tell the server where we are.
            _quickLoadInProgress = true;
            WarpSystem.Singleton.HoldSubspaceRequest = true;
            WarpSystem.Singleton.CurrentSubspace = -1;

            //Remove our vessel and everything that came off it since the quicksave, for us and for everybody else
            var familyIds = new HashSet<Guid>(familyVessels.Select(v => v.id));
            foreach (var vesselId in familyIds)
            {
                VesselRemoveSystem.Singleton.MessageSender.SendVesselRemove(vesselId, true, "Quickload");
                VesselRemoveSystem.Singleton.RemovedVessels.TryAdd(vesselId, LunaNetworkTime.UtcNow);
                VesselCommon.RemoveVesselFromSystems(vesselId);
                //Release the locks a bit later so the remove message gets to the other players first
                LockSystem.Singleton.ReleaseAllVesselLocks(null, vesselId, 1f);
            }

            foreach (var crew in revivedCrew)
                KerbalSystem.Singleton.MessageSender.SendKerbal(crew.Key);

            game.flightState.protoVessels.RemoveAll(pv => pv == null || familyIds.Contains(pv.vesselID));
            game.flightState.universalTime = snapshot.GameTime;
            game.flightState.protoVessels.Add(protoVessel);

            snapshot.VesselId = newVesselId;
            snapshot.DockedWithOutsider = false;

            //Reload the flight scene focused on the restored vessel, the same way stock does after a quickload.
            //Once the flight is ready the vessel is sent to the server and we take its locks, as with any vessel we fly.
            FlightDriver.StartAndFocusVessel(game, game.flightState.protoVessels.Count - 1);
        }

        private QuickLoadState BuildState(Vessel[] vessels, Vessel[] familyVessels)
        {
            var playerName = SettingsSystem.CurrentSettings.PlayerName;
            var familyIds = new HashSet<Guid>(familyVessels.Select(v => v.id));

            return new QuickLoadState
            {
                FeatureEnabled = FeatureEnabled,
                HasSnapshot = Snapshot != null,
                Spectating = VesselCommon.IsSpectating,
                Warping = _quickLoadInProgress || WarpSystem.Singleton.CurrentlyWarping || TimeWarp.CurrentRateIndex > 0,
                WaitingSubspaceId = WarpSystem.Singleton.WaitingSubspaceIdFromServer,
                FamilyVesselLockedByOther = familyVessels.Any(v => LockSystem.LockQuery.ControlLockExists(v.id) &&
                                                                   !LockSystem.LockQuery.ControlLockBelongsToPlayer(v.id, playerName)),
                DockedWithOutsider = (Snapshot?.DockedWithOutsider ?? false) || familyVessels.Any(v => RelationOf(v) == FamilyRelation.MemberWithOutsideParts),
                SnapshotCrew = Snapshot?.Crew,
                CrewAboardFamily = familyVessels.SelectMany(CrewNamesOf).ToArray(),
                CrewAboardOtherVessels = vessels.Where(v => !familyIds.Contains(v.id)).SelectMany(CrewNamesOf).ToArray(),
                SnapshotTime = Snapshot?.GameTime ?? 0,
                CurrentTime = Planetarium.GetUniversalTime(),
                MaxRewindMinutes = SettingsSystem.ServerSettings.QuickLoadMaxRewindMinutes
            };
        }

        private static IEnumerable<string> CrewNamesOf(Vessel vessel)
        {
            var crew = vessel.loaded ? vessel.GetVesselCrew() : vessel.protoVessel?.GetVesselCrew();
            return crew?.Where(c => c != null).Select(c => c.name) ?? Enumerable.Empty<string>();
        }

        private static string RefusalText(QuickLoadRefusal refusal)
        {
            switch (refusal)
            {
                case QuickLoadRefusal.NoSnapshot: return LocalizationContainer.ScreenText.QuickLoadNoSnapshot;
                case QuickLoadRefusal.Spectating: return LocalizationContainer.ScreenText.QuickLoadSpectating;
                case QuickLoadRefusal.WarpingOrWaitingSubspace: return LocalizationContainer.ScreenText.QuickLoadWarping;
                case QuickLoadRefusal.LockedByOtherPlayer: return LocalizationContainer.ScreenText.QuickLoadLocked;
                case QuickLoadRefusal.DockedWithOutsideVessel: return LocalizationContainer.ScreenText.QuickLoadDocked;
                case QuickLoadRefusal.CrewChanged: return LocalizationContainer.ScreenText.QuickLoadCrewChanged;
                case QuickLoadRefusal.SnapshotTooOld: return LocalizationContainer.ScreenText.QuickLoadTooOld;
                default: return LocalizationContainer.ScreenText.CannotLoadGames;
            }
        }

        private static void Show(string text)
        {
            LunaScreenMsg.PostScreenMessage(text, 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        #endregion
    }
}
