using LmpClient.Base;
using LmpCommon.QuickLoad;

namespace LmpClient.Systems.QuickLoad
{
    public class QuickLoadEvents : SubSystem<QuickLoadSystem>
    {
        /// <summary>
        /// Stock fires this only once it has accepted a quicksave (F5), just before writing its own file
        /// </summary>
        public void OnGameAboutToQuicksave()
        {
            System.TakeSnapshot();
        }

        /// <summary>
        /// Remember when our vessel couples with a vessel from outside. Resources, science or crew could have moved
        /// between them, so a quickload after that could duplicate them even if they undock again.
        /// </summary>
        public void OnPartCoupling(Part partFrom, Part partTo)
        {
            if (System.Snapshot == null || partFrom == null || partTo == null) return;

            if (QuickLoadFamily.IsCoupleWithOutsider(System.RelationOf(partFrom.vessel), System.RelationOf(partTo.vessel)))
                System.Snapshot.DockedWithOutsider = true;
        }

        /// <summary>
        /// The flight scene we reloaded for a quickload has started
        /// </summary>
        public void OnFlightStarted()
        {
            System.FinishQuickLoad(true);
        }

        /// <summary>
        /// A recovered or terminated vessel must never come back with a quickload
        /// </summary>
        public void OnVesselRecoveredOrTerminated(ProtoVessel protoVessel)
        {
            if (System.Snapshot != null && System.RelationOf(protoVessel) != FamilyRelation.Outside)
                System.ClearSnapshot();
        }
    }
}
