using System;
using System.Linq;

namespace LmpCommon.QuickLoad
{
    public enum QuickLoadRefusal
    {
        None,
        FeatureDisabled,
        NoSnapshot,
        Spectating,
        WarpingOrWaitingSubspace,
        LockedByOtherPlayer,
        DockedWithOutsideVessel,
        CrewChanged,
        SnapshotTooOld
    }

    public struct QuickLoadState
    {
        /// <summary>Server allows quickload and the warp mode is subspace</summary>
        public bool FeatureEnabled;
        public bool HasSnapshot;
        public bool Spectating;
        public bool Warping;
        public bool WaitingSubspaceId;
        public bool FamilyVesselLockedByOther;
        public bool DockedWithOutsider;
        public string[] SnapshotCrew;
        /// <summary>Crew currently aboard vessels that belong to the family</summary>
        public string[] CrewAboardFamily;
        /// <summary>Crew currently aboard any vessel outside the family</summary>
        public string[] CrewAboardOtherVessels;
        public double SnapshotTime;
        public double CurrentTime;
        /// <summary>0 or less means no limit</summary>
        public int MaxRewindMinutes;
    }

    public static class QuickLoadCheck
    {
        /// <summary>
        /// True when the reason of a vessel remove message says the vessel was recovered or terminated
        /// </summary>
        public static bool IsRecoveryReason(string removeReason)
        {
            return removeReason == "Recovered" || removeReason == "Terminated" || removeReason == "Recovered/Terminated";
        }

        /// <summary>
        /// Returns the first reason why a quickload must be refused, or None when it can go ahead
        /// </summary>
        public static QuickLoadRefusal Decide(QuickLoadState state)
        {
            if (!state.FeatureEnabled) return QuickLoadRefusal.FeatureDisabled;
            if (!state.HasSnapshot) return QuickLoadRefusal.NoSnapshot;
            if (state.Spectating) return QuickLoadRefusal.Spectating;
            if (state.Warping || state.WaitingSubspaceId) return QuickLoadRefusal.WarpingOrWaitingSubspace;
            if (state.FamilyVesselLockedByOther) return QuickLoadRefusal.LockedByOtherPlayer;
            if (state.DockedWithOutsider) return QuickLoadRefusal.DockedWithOutsideVessel;

            var snapshotCrew = state.SnapshotCrew ?? Array.Empty<string>();
            var crewAboardFamily = state.CrewAboardFamily ?? Array.Empty<string>();
            var crewAboardOthers = state.CrewAboardOtherVessels ?? Array.Empty<string>();

            if (snapshotCrew.Any(c => crewAboardOthers.Contains(c)) || crewAboardFamily.Any(c => !snapshotCrew.Contains(c)))
                return QuickLoadRefusal.CrewChanged;

            if (state.MaxRewindMinutes > 0 && state.CurrentTime - state.SnapshotTime > state.MaxRewindMinutes * 60d)
                return QuickLoadRefusal.SnapshotTooOld;

            return QuickLoadRefusal.None;
        }
    }
}
