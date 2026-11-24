using LmpCommon.Enums;
using LmpCommon.Xml;
using System;

namespace Server.Settings.Definition
{
    [Serializable]
    public class WarpSettingsDefinition
    {
        [XmlComment(Value = "Specify the warp Type. Values: None, Subspace")]
        public WarpMode WarpMode { get; set; } = WarpMode.Subspace;

        [XmlComment(Value = "Allow players to quicksave (F5) and quickload (F9) the vessel they are flying. A quickload puts the player in an earlier subspace. Only works with WarpMode Subspace")]
        public bool AllowQuickLoad { get; set; } = false;

        [XmlComment(Value = "Maximum age in minutes of a quicksave that can still be quickloaded. 0 = no limit. This limit is enforced by the client")]
        public int QuickLoadMaxRewindMinutes { get; set; } = 60;
    }
}
