namespace MachinePowerLayout
{
    /// <summary>
    /// ModConfig entries. Labels/tooltips are Chinese (rendered by the game font);
    /// anything that can reach the log stays ASCII.
    /// </summary>
    public class Settings
    {
        public ConfigEntry<string> Hotkey;
        public ConfigEntry<string> Mode;
        public ConfigEntry<bool> DryRun;
        public ConfigEntry<bool> AllowRotation;
        public ConfigEntry<int> ChargeOverride;
        public ConfigEntry<int> BudgetMs;
        public ConfigEntry<bool> IncludeBatteries;
        public ConfigEntry<bool> AutoBeforeSleep;
        public ConfigEntry<bool> Verbose;
        public ConfigEntry<bool> ShowToast;
        public ConfigEntry<bool> OpenWindowsOnly;
        public ConfigEntry<bool> AllowMachineTransfer;

        public Settings(string modId)
        {
            ModConfig c = ModConfig.For(modId);

            Hotkey = c.Define("hotkey", "F7", "1 \u5e38\u89c4",
                () => "\u89e6\u53d1\u5feb\u6377\u952e",
                new[] { "F5", "F6", "F7", "F8", "F9", "F10", "F11" },
                () => "\u5728\u5e97\u94fa\u5185\u6309\u4e0b\u6b64\u952e\uff0c\u5bf9\u673a\u5668\u533a\u6267\u884c\u4e00\u6b21\u4f9b\u7535\u5e03\u5c40\u4f18\u5316\u3002",
                false);

            Mode = c.Define("mode", "ArraysOnly", "1 \u5e38\u89c4",
                () => "\u5e03\u5c40\u6a21\u5f0f",
                new[] { "ArraysOnly", "FullRelayout" },
                () => "ArraysOnly: \u53ea\u632a\u52a8\u80fd\u91cf\u9635\u5217\uff08\u5b89\u5168\uff0c\u4e0d\u52a8\u673a\u5668\uff09\u3002"
                    + "FullRelayout: \u8fde\u673a\u5668\u4e00\u8d77\u91cd\u6392\uff08\u9700\u8981\u8db3\u591f\u7a7a\u683c\uff0c\u5931\u8d25\u4f1a\u81ea\u52a8\u56de\u6eda\uff09\u3002",
                false);

            DryRun = c.Define("dry_run", false, "1 \u5e38\u89c4",
                () => "\u53ea\u8ba1\u7b97\u4e0d\u843d\u5b9e",
                () => "\u5f00\u542f\u540e\u53ea\u5728\u65e5\u5fd7\u91cc\u8f93\u51fa\u65b9\u6848\u4e0e\u9884\u671f\u6536\u76ca\uff0c\u4e0d\u79fb\u52a8\u4efb\u4f55\u7269\u54c1\u3002",
                false);

            AllowRotation = c.Define("allow_rotation", false, "2 \u6c42\u89e3\u5668",
                () => "\u5141\u8bb8\u65cb\u8f6c",
                () => "\u5141\u8bb8\u5c06\u77e9\u5f62\u673a\u5668\uff08\u5982\u51c0\u6c34\u5668 3x4\uff09\u65cb\u8f6c 90 \u5ea6\u4ee5\u6362\u53d6\u66f4\u4f18\u5e03\u5c40\u3002",
                false);

            ChargeOverride = c.Define("charge_per_array", 0, "2 \u6c42\u89e3\u5668",
                () => "\u5355\u53f0\u9635\u5217\u6bcf\u665a\u5145\u7535\u91cf (\u0030=\u81ea\u52a8)",
                (int?)0, (int?)64,
                () => "\u9ed8\u8ba4\u81ea\u52a8\uff1a\u672a\u89e3\u9501\u5546\u7528\u4f9b\u7535\u65f6\u4e3a 2\uff0c\u5df2\u89e3\u9501\u65f6\u4e3a 4\u3002",
                false);

            BudgetMs = c.Define("budget_ms", 350, "2 \u6c42\u89e3\u5668",
                () => "\u641c\u7d22\u65f6\u95f4\u4e0a\u9650 (ms)",
                (int?)100, (int?)5000,
                () => "\u5355\u6b21\u6c42\u89e3\u7684\u58c1\u949f\u4e0a\u9650\u3002\u8d85\u65f6\u5373\u8f93\u51fa\u5f53\u524d\u6700\u4f18\u89e3\u3002",
                false);

            IncludeBatteries = c.Define("include_batteries", true, "2 \u6c42\u89e3\u5668",
                () => "\u628a\u6563\u88c5\u7535\u6c60\u4e5f\u5f53\u76ee\u6807",
                () => "\u80fd\u91cf\u9635\u5217\u540c\u6837\u4f1a\u7ed9\u76f8\u90bb\u7684\u72ec\u7acb\u7535\u6c60\u5145\u7535\uff1b\u5f00\u542f\u540e\u4f59\u91cf\u4f1a\u4f18\u5148\u7167\u987e\u7535\u6c60\u3002",
                false);

            AutoBeforeSleep = c.Define("auto_before_sleep", false, "3 \u81ea\u52a8\u5316",
                () => "\u7761\u89c9\u524d\u81ea\u52a8\u4f18\u5316",
                () => "\u6bcf\u665a\u8fdb\u5165\u8fc7\u591c\u6d41\u7a0b\u524d\u81ea\u52a8\u8dd1\u4e00\u6b21\uff08\u4ec5\u6309\u5f53\u524d\u6a21\u5f0f\u6267\u884c\uff0c\u4e0d\u53d7\u300c\u53ea\u8ba1\u7b97\u4e0d\u843d\u5b9e\u300d\u5f71\u54cd\uff09\u3002",
                false);

            Verbose = c.Define("verbose", true, "3 \u81ea\u52a8\u5316",
                () => "\u8be6\u7ec6\u65e5\u5fd7",
                () => "\u8f93\u51fa\u9010\u53f0\u673a\u5668\u7684\u4f9b\u9700\u660e\u7ec6\u4e0e\u5e03\u5c40\u56fe\u3002",
                false);

            ShowToast = c.Define("show_toast", true, "3 \u81ea\u52a8\u5316",
                () => "\u5c4f\u5e55\u63d0\u793a",
                () => "\u6c42\u89e3\u5b8c\u6210\u540e\u5728\u5c4f\u5e55\u5de6\u4e0a\u89d2\u663e\u793a 5 \u79d2\u7ed3\u679c\u6458\u8981\u3002",
                false);

            AllowMachineTransfer = c.Define("allow_machine_transfer", true, "2 \u6c42\u89e3\u5668",
                () => "FullRelayout \u65f6\u5141\u8bb8\u673a\u5668\u8de8\u533a",
                () => "\u5f00\u542f\u540e\uff0c\u6ee1\u8db3\u4f9b\u7535\u7684\u53e6\u4e00\u4e2a\u673a\u5668\u533a\u53ef\u4ee5\u63a5\u6536\u4f60\u7f3a\u7535\u7684\u673a\u5668\uff08\u8fde\u540c\u5185\u90e8\u7269\u54c1\u4e00\u8d77\u642c\uff09\u3002",
                false);

            OpenWindowsOnly = c.Define("open_windows_only", true, "1 \u5e38\u89c4",
                () => "\u53ea\u5904\u7406\u5f53\u524d\u6253\u5f00\u7684\u673a\u5668\u533a",
                () => "\u5f00\u542f\u540e\u53ea\u5bf9\u5c4f\u5e55\u4e0a\u5df2\u6253\u5f00\u7684\u673a\u5668\u533a\u7a97\u53e3\u505a\u5e03\u5c40\u4e0e\u8de8\u533a\u8c03\u5ea6\uff1b"
                    + "\u82e5\u4e00\u4e2a\u4e5f\u6ca1\u68c0\u6d4b\u5230\uff0c\u81ea\u52a8\u56de\u9000\u4e3a\u5904\u7406\u5168\u90e8\u3002",
                false);
        }

        public bool FullMode { get { return Mode.Value == "FullRelayout"; } }
    }
}
