using UnityEngine;

namespace MachinePowerLayout
{
    /// <summary>
    /// Loader entry point. The loader runs Harmony.PatchAll before Init, so nothing
    /// here may assume the game world exists yet.
    /// </summary>
    public class ModMain : IMod
    {
        public static ModLog Log;
        public static Settings Cfg;
        public static string ModId = "machine_power_layout";

        private GameObject host;

        public void Init(ModManifest manifest)
        {
            ModId = manifest.ID;

            // The log prefix is manifest.Name copied at construction time. Use an ASCII
            // name for the prefix, then restore the displayed Chinese name.
            string display = manifest.Name;
            manifest.Name = "PowerLayout";
            Log = new ModLog(manifest);
            manifest.Name = display;

            Cfg = new Settings(ModId);
        }

        public void OnEnable()
        {
            if (host != null) return;
            host = new GameObject("MachinePowerLayout_Runner");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<Runner>();
            Log.Log("enabled. hotkey=" + Cfg.Hotkey.Value + " mode=" + Cfg.Mode.Value
                    + " dry_run=" + Cfg.DryRun.Value + " rotation=" + Cfg.AllowRotation.Value);
        }

        public void OnDisable()
        {
            if (host != null)
            {
                Object.Destroy(host);
                host = null;
            }
            Log.Log("disabled.");
        }
    }
}
