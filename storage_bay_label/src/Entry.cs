using System.Text;

public class StorageBayLabelMod : IMod
{
    // Tag key that carries the suffix. Written with skipValidate so the game never
    // rejects it as "unknown tag for this item type".
    internal const string LabelTag = "mod_storage_bay_label";

    internal const int MaxLabelLength = 12;

    internal static ModLog Log;

        public void Init(ModManifest manifest)
        {
            string displayName = manifest.Name;
            manifest.Name = "StorageBayLabel"; // ASCII only: this becomes the log prefix
            Log = new ModLog(manifest);
            manifest.Name = displayName;

            try { Sbl.Settings.Init(manifest.ID); }
            catch (System.Exception e) { SafeError("config init failed: " + e.Message); }

            SafeLog("init v" + manifest.ModVersion);
        }

    public void OnEnable()
    {
        try { Sbl.Overlay.Reset(); } catch { }

        bool skin = false;
        try { skin = Sbl.BaySkin.HasAnySkin(); } catch { }

        SafeLog("enabled; baySkin=" + (skin ? "ok" : "missing"));
    }

    public void OnDisable()
    {
        try { Sbl.Overlay.Reset(); } catch { }
        SafeLog("disabled");
    }

    internal static void SafeLog(string message)
    {
        try { if (Log != null) Log.Log(message); } catch { }
    }

    internal static void SafeWarning(string message)
    {
        try { if (Log != null) Log.Warning(message); } catch { }
    }

    internal static void SafeError(string message)
    {
        try { if (Log != null) Log.Error(message); } catch { }
    }

    /// <summary>
    /// The F8 console font has no CJK glyphs, so anything user-typed must be
    /// logged in a lossy ASCII form or the whole line renders blank.
    /// </summary>
    internal static string Ascii(string text)
    {
        if (string.IsNullOrEmpty(text)) return "<empty>";
        StringBuilder sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(c < 32 || c > 126 ? '?' : c);
        }
        return sb.ToString();
    }
}
