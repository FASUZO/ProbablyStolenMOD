using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModuleSoloUpgrade
{
    // -------------------------------------------------------------------------
    //  Keeps the two machinery buttons showing the electricity the NEXT press
    //  will actually cost.
    //
    //  The cost is not a constant. "Learn" charges PowerCost * steps, but steps
    //  are clamped to the module's remaining gap, so the real number shrinks as
    //  the module approaches its cap; and nothing is charged while a
    //  precondition fails (no module, no bonus to learn, both effect slots
    //  taken, no battery). Showing a fixed "-10" would therefore be wrong.
    //
    //  The driver refreshes a few times per second instead of only on click, so
    //  inserting a module or a battery updates the numbers without any input.
    // -------------------------------------------------------------------------
    internal static class DeviceUi
    {
        private const float Interval = 0.25f;

        private sealed class Entry
        {
            public GameItem Device;
            public MachineryButton Implant;
            public MachineryButton Learn;
            public string ImplantCaption;
            public string LearnCaption;
            public string ImplantTip;
            public string LearnTip;
        }

        private static readonly Dictionary<GameItem, Entry> Entries = new Dictionary<GameItem, Entry>();
        private static UiDriver _driver;

        internal static void Register(GameItem device, MachineryButton implant, MachineryButton learn)
        {
            if (device == null || implant == null || learn == null) return;
            try
            {
                var e = new Entry { Device = device, Implant = implant, Learn = learn };
                Entries[device] = e;
                Refresh(e);
            }
            catch (Exception ex)
            {
                ModEntry.Log.Warning("UI register failed: " + ex.Message);
            }
        }

        internal static void EnsureDriver()
        {
            if (_driver != null) return;
            try
            {
                var go = new GameObject("ModuleSoloUpgradeUi");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _driver = go.AddComponent<UiDriver>();
                ModEntry.Log.Log("UI driver started.");
            }
            catch (Exception ex)
            {
                ModEntry.Log.Warning("UI driver could not start: " + ex.Message);
            }
        }

        internal static void RefreshAll()
        {
            if (Entries.Count == 0) return;
            List<GameItem> dead = null;
            foreach (var kv in Entries)
            {
                var e = kv.Value;
                // A destroyed GameItem compares equal to null through Unity's
                // overload, which is how a removed device is detected.
                if (e == null || e.Device == null || e.Implant == null || e.Learn == null)
                {
                    if (dead == null) dead = new List<GameItem>();
                    dead.Add(kv.Key);
                    continue;
                }
                Refresh(e);
            }
            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++) Entries.Remove(dead[i]);
            }
        }

        private static void Refresh(Entry e)
        {
            int implantCost, learnSteps, learnCost, power;
            bool hasBattery;
            DeviceLogic.Predict(e.Device, out implantCost, out learnSteps, out learnCost,
                                out power, out hasBattery);

            string implantCaption = implantCost > 0
                ? "植入自学习 -" + implantCost
                : "植入自学习";
            string learnCaption = learnCost > 0
                ? "自学习 +" + learnSteps + " -" + learnCost
                : "自学习 +" + Math.Max(1, ModEntry.StepPerUse);

            string powerText;
            if (!hasBattery) powerText = "电量槽为空。";
            else if (power <= 0) powerText = "电池已耗尽。";
            else powerText = "电池剩余 " + power + " 点。";
            string implantTip = "为模块植入「自学习」词条。"
                + (implantCost > 0 ? "本次消耗 " + implantCost + " 点电量。" : "当前没有可植入的模块。")
                + powerText;
            string learnTip = "推进一次自学习进度，每次推进 1 点。"
                + (learnCost > 0 ? "本次推进 +" + learnSteps + "，消耗 " + learnCost + " 点电量。" : "当前没有可推进的模组。")
                + powerText;

            // Setting .text rebuilds the button's text node, so only write on change.
            if (e.ImplantCaption != implantCaption)
            {
                e.Implant.SetText(implantCaption);
                e.ImplantCaption = implantCaption;
            }
            if (e.LearnCaption != learnCaption)
            {
                e.Learn.SetText(learnCaption);
                e.LearnCaption = learnCaption;
            }
            if (e.ImplantTip != implantTip)
            {
                e.Implant.SetDescription(implantTip);
                e.ImplantTip = implantTip;
            }
            if (e.LearnTip != learnTip)
            {
                e.Learn.SetDescription(learnTip);
                e.LearnTip = learnTip;
            }
        }
    }

    // Runs on its own DontDestroyOnLoad object so the refresh survives scene
    // changes (main menu <-> shop).
    internal sealed class UiDriver : MonoBehaviour
    {
        private float _next;

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.25f;
            try { DeviceUi.RefreshAll(); }
            catch (Exception ex) { ModEntry.Log.Warning("UI refresh failed: " + ex.Message); }
        }
    }
}
