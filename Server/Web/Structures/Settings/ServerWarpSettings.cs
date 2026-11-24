using Server.Settings.Structures;

namespace Server.Web.Structures.Settings
{
    public class ServerWarpSettings
    {
        public string WarpMode => WarpSettings.SettingsStore.WarpMode.ToString();
        public bool AllowQuickLoad => WarpSettings.SettingsStore.AllowQuickLoad;
        public int QuickLoadMaxRewindMinutes => WarpSettings.SettingsStore.QuickLoadMaxRewindMinutes;
    }
}
