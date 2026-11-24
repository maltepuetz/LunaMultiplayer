namespace LmpClient.Localization.Structures
{
    public class ScreenText
    {
        public string CraftUploaded { get; set; } = "Craft uploaded!";
        public string CraftSaved { get; set; } = "Craft saved!";
        public string ModFileGenerated { get; set; } = "LMPModControl.xml file generated in your KSP folder";
        public string Disconected { get; set; } = "You have been disconnected!";
        public string Spectating { get; set; } = "This vessel is being controlled by";
        public string SafetyBubble { get; set; } = "Remember!! While you're inside the safety bubble you won't be seen by other players!!";
        public string CheckParts { get; set; } = "If you use mod or DLC parts that other players don't have you won't be seen by them!";
        public string CannotRecover { get; set; } = "Cannot recover vessel, the vessel is not yours.";
        public string CannotTerminate { get; set; } = "Cannot terminate vessel, the vessel is not yours.";
        public string SpectatingRemoved { get; set; } = "The vessel you were spectating was removed";
        public string WarpDisabled { get; set; } = "Cannot warp, warping is disabled on this server";
        public string WaitingSubspace { get; set; } = "Cannot warp, waiting subspace id from the server";
        public string CannotWarpWhileSpectating { get; set; } = "Cannot warp while spectating";
        public string ScreenshotInterval { get; set; } = "Interval between screenshots is $1 seconds. Cannot upload the screenshot at this moment";
        public string CraftLibraryInterval { get; set; } = "Interval between craft library requests is $1 seconds. Cannot process the request at this moment";
        public string ScreenshotTaken { get; set; } = "Screenshot uploaded!";
        public string ImageSaved { get; set; } = "Image saved to GameData/LunaMultiplayer/Screenshots";
        public string IncreasedInterpolationOffset { get; set; } = "Warning! Your interpolation offset has been increased as it was too low for this server";
        public string SackingKerbalsNotAllowed { get; set; } = "This server does not allow firing kerbals";
        public string CannotLoadGames { get; set; } = "LMP does not allow loading savegames";
        public string KerbalNotYours { get; set; } = "Another player is using this kerbal";
        public string UnsafeToSync { get; set; } = "Cannot sync while in unstable orbit or spectating as you might crash! You can turn off this check in settings";
        public string QuickSaved { get; set; } = "Quicksaved (multiplayer)";
        public string QuickLoaded { get; set; } = "Quickloaded: you are now in an earlier subspace";
        public string QuickSaveNotOwnVessel { get; set; } = "Cannot quicksave: you are not controlling this vessel";
        public string QuickSaveEva { get; set; } = "Cannot quicksave a kerbal on EVA";
        public string QuickLoadNoSnapshot { get; set; } = "No multiplayer quicksave to load";
        public string QuickLoadSpectating { get; set; } = "Cannot quickload while spectating";
        public string QuickLoadWarping { get; set; } = "Cannot quickload while warping";
        public string QuickLoadLocked { get; set; } = "Cannot quickload: another player controls part of your vessel";
        public string QuickLoadDocked { get; set; } = "Cannot quickload: your vessel docked with another vessel since the quicksave";
        public string QuickLoadCrewChanged { get; set; } = "Cannot quickload: crew moved to or from another vessel since the quicksave";
        public string QuickLoadFailed { get; set; } = "Quickload failed. See the log for details";
        public string QuickLoadTooOld { get; set; } = "Cannot quickload: the quicksave is too old for this server";
    }
}
