using LmpCommon.QuickLoad;
using System;

namespace LmpClient.Systems.QuickLoad
{
    /// <summary>
    /// What we remember when the player quicksaves: the vessel being flown, when it was, and who was on board
    /// </summary>
    public class QuickLoadSnapshot
    {
        public double GameTime;
        public ConfigNode VesselNode;
        public Guid VesselId;
        public string[] Crew;
        public QuickLoadFamily Family;
        /// <summary>
        /// Set once the vessel, or something that came off it, coupled with a vessel from outside since the quicksave
        /// </summary>
        public bool DockedWithOutsider;
    }
}
