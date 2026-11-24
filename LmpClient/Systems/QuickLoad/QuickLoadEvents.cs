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
        /// A recovered or terminated vessel must never come back with a quickload
        /// </summary>
        public void OnVesselRecoveredOrTerminated(ProtoVessel protoVessel)
        {
            if (System.Snapshot != null && System.RelationOf(protoVessel) != FamilyRelation.Outside)
                System.ClearSnapshot();
        }
    }
}
