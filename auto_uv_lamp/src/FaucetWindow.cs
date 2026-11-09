using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoUvLamp
{
    /// <summary>
    /// 给水龙头挂一个「紫外线灯」槽位：把 uv_filter 从背包拖进槽位 = 装配，拖出来 = 卸下。
    /// 全程在游戏里操作，不需要打开任何 mod 设置；水龙头槽位里的灯本身就是唯一的状态。
    ///
    /// 为什么需要自建窗口：水龙头本体（MiscItemDirectory.Faucet）只是一个带 sprite 和标签的纯
    /// GameItem，没有任何内容窗口；而 EmporiumEntry.faucetElement 那个 GameSlotInventory 装的是
    /// 水龙头本体自己（AddAlwayItems 里 TryAcceptAll(faucetElement, faucet)，与 dossier/trashcan/drain
    /// 同一套路），玩家放不进第二件东西。所以这里用游戏自己的 MachineHelper 造一个机械窗口
    /// （与售水机同构），挂到水龙头上。
    ///
    /// 存档：水龙头是固定设施、不在存档的物品列表里，所以它窗口内的物品**不会被游戏保存**。
    /// 因此用 PlayerStore.EncodeItem 把灯编码成引擎格式，写进 ModHelper.SetModData
    /// （落在 PlayerStore.modData，随存档槽文件序列化，天然按存档隔离），读档时用
    /// SaveManager.DecodeNodes 还原。
    /// </summary>
    internal sealed class FaucetWindow : MonoBehaviour
    {
        const string Atlas = "Items/items_amenities";
        const string UvSprite = "uv";
        const string LampId = "uv_filter";
        const string PanelTitle = "紫外线灯";
        const string ModDataKey = "faucet_lamp";
        /// <summary>没有装配时的占位值。写空串可能被 modData 当成删除，用一个空 JSON 更稳。</summary>
        const string EmptyBlob = "{}";
        /// <summary>
        /// 进店后先等这么久再自建窗口。别的模组（例如「更好的水龙头」）会在进店后立刻
        /// 给水龙头挂窗口，而水龙头只能有一个 contentWindow —— 谁先建谁赢。让几秒，
        /// 保证「有更完整的龙头模组时优先用它」，本 mod 退化为只读。
        /// </summary>
        const float GiveWaySeconds = 3f;

        static FaucetWindow _instance;

        GameItem _faucet;
        EmporiumEntry _entry;
        GameSlotInventory _slot;
        bool _built;
        bool _loading;
        bool _foreignLogged;
        int _contentSlot = -1;
        string _lastBlob;
        float _nextSave;
        float _entrySince = -1f;

        // ---------------------------------------------------------------- 生命周期

        internal static void Attach()
        {
            if (_instance != null) return;
            try
            {
                var go = new GameObject("AutoUvLamp.FaucetWindow");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<FaucetWindow>();
                AutoUvLampMod.Info("faucet slot: driver created");
            }
            catch (Exception e)
            {
                AutoUvLampMod.Warning("faucet slot: driver failed: " + e.Message);
            }
        }

        internal static void Detach()
        {
            try
            {
                if (_instance != null) UnityEngine.Object.Destroy(_instance.gameObject);
            }
            catch { }
            _instance = null;
        }

        void Update()
        {
            try
            {
                if (!_built)
                {
                    TryBuild();
                    return;
                }

                var entry = EmporiumEntry.Instance;
                if (entry == null || entry != _entry)
                {
                    // 场景卸载或重载：旧水龙头对象连同窗口一起没了，下次进店重建。
                    _built = false;
                    _entry = null;
                    _faucet = null;
                    _slot = null;
                    _entrySince = -1f;
                    return;
                }

                if (Time.unscaledTime < _nextSave) return;
                _nextSave = Time.unscaledTime + 2f;
                Save();
            }
            catch (Exception e)
            {
                _built = false;
                // 重置让路计时器：失败后至少再等 GiveWaySeconds 才重试，避免每帧刷日志。
                _entrySince = -1f;
                AutoUvLampMod.Warning("faucet slot: update failed: " + e.Message);
            }
        }

        // ---------------------------------------------------------------- 构建

        void TryBuild()
        {
            var entry = EmporiumEntry.Instance;
            if (entry == null)
            {
                _entrySince = -1f;
                return;
            }

            GameItem faucet;
            try { faucet = entry.faucet; }
            catch { return; }
            if (faucet == null)
            {
                _entrySince = -1f;
                return;
            }

            if (_entrySince < 0f) _entrySince = Time.unscaledTime;

            // 让路：窗口还不是我们建的，就先等几秒看有没有别的模组会建。
            if (faucet.contentWindow == null && Time.unscaledTime - _entrySince < GiveWaySeconds) return;

            Build(entry, faucet);
        }

        void Build(EmporiumEntry entry, GameItem faucet)
        {
            _entry = entry;
            _faucet = faucet;
            _contentSlot = CurrentSlotId();
            // 先当作「空槽」：这样首次进店且没装灯时不会白写一次 modData（也不会刷一条日志）。
            // LoadLamp 读回存档里的灯时会把这里覆盖成实际内容。
            _lastBlob = EmptyBlob;
            _slot = null;

            var window = faucet.contentWindow;
            if (window == null)
            {
                BuildWindow(faucet);
                LoadLamp();
            }
            else if (!_foreignLogged)
            {
                // 别的模组已经给水龙头建过窗口（例如「更好的水龙头」）。
                // 不重建、不接管，只做只读兜底：Lamp() 会在它的槽位里找 uv_filter。
                _foreignLogged = true;
                AutoUvLampMod.Info("faucet slot: faucet already has a content window from another mod;"
                                   + " leaving it alone. Install the UV lamp in THAT window's UV slot"
                                   + " (it may stay locked until that mod's retrofit is unlocked).");
            }

            _built = true;
            AutoUvLampMod.Info("faucet slot: ready, lamp=" + (Lamp() != null ? "installed" : "none"));
        }

        void BuildWindow(GameItem faucet)
        {
            // uv_filter 的形状是 5×1，槽位按同样尺寸建，拖入时形状才匹配。
            var slot = new GameSlotInventory(5, 1, false, false);
            slot.SetMachineryStyle();
            try { slot.SetBackgroundFadeSprite(Atlas, UvSprite, 0.45f, 0.45f); } catch { }

            // 只收紫外线灯：这是「装配」的唯一门控，别的物品拖不进来。
            slot.mayInventoryAddItemFunc = (item, inv) => item != null && item.identifier == LampId;

            var panel = MachineHelper.Panel(PanelTitle, slot, "uv", true);
            var panels = new[] { panel };
            var window = MachineHelper.CreateMachineryWindow(true, panels);
            faucet.SetContentWindow(window);

            _slot = slot;
            AutoUvLampMod.Info("faucet slot: window built (accepts uv_filter only)");
        }

        // ---------------------------------------------------------------- 查询

        /// <summary>
        /// 水龙头上当前装着的紫外线灯；没装返回 null。
        /// 优先读本模组建的槽位；读不到再在窗口里找任意 uv_filter（兼容别的模组建的槽位）。
        /// </summary>
        internal static GameItem Lamp()
        {
            var self = _instance;
            if (self == null) return null;

            var slot = self._slot;
            if (slot != null)
            {
                try
                {
                    var items = slot.childItems;
                    if (items != null)
                    {
                        foreach (var it in items)
                        {
                            if (it != null && it.identifier == LampId) return it;
                        }
                    }
                }
                catch { }
            }

            try
            {
                var faucet = self._faucet;
                if (faucet == null) return null;
                var window = faucet.contentWindow;
                if (window == null) return null;

                var found = GraphUtils.FindAllChildrenType<GameItem>(
                    window, it => it != null && it.identifier == LampId, null);
                if (found != null && found.Count > 0) return found[0];
            }
            catch { }

            return null;
        }

        /// <summary>水龙头上是否装了紫外线灯。</summary>
        internal static bool HasLamp()
        {
            return Lamp() != null;
        }

        // ---------------------------------------------------------------- 存档

        /// <summary>存档前同步一次（由 PlayerStore.SaveGame 的前置补丁调用）。</summary>
        internal static void SaveNow()
        {
            try
            {
                if (_instance != null && _instance._built) _instance.Save();
            }
            catch { }
        }

        /// <summary>
        /// 清空我们自己那格里的物品（切换存档时用）。
        /// 只动本模组建的槽位；别人模组的窗口一律不碰。
        /// </summary>
        void ClearSlot()
        {
            var slot = _slot;
            if (slot == null) return;

            try
            {
                var items = slot.childItems;
                if (items == null) return;

                var copy = new List<GameItem>(items);
                foreach (var it in copy)
                {
                    if (it != null) { try { it.Destroy(); } catch { } }
                }
                AutoUvLampMod.Info("faucet slot: cleared (save slot changed)");
            }
            catch (Exception e)
            {
                AutoUvLampMod.Warning("faucet slot: clear failed: " + e.Message);
            }
        }

        void Save()
        {
            if (_loading) return;

            // 新建/切换存档的瞬间 saveSlotId 已变但槽里还是旧档内容：清掉，避免带进新档。
            int slotNow = CurrentSlotId();
            if (_contentSlot >= 0 && slotNow >= 0 && slotNow != _contentSlot)
            {
                _contentSlot = slotNow;
                ClearSlot();
                _lastBlob = EmptyBlob;
                return;
            }

            var lamp = Lamp();
            string blob = EmptyBlob;
            if (lamp != null)
            {
                string encoded;
                try { encoded = Encode(lamp); }
                catch (Exception e)
                {
                    AutoUvLampMod.Warning("faucet slot: encode failed: " + e.Message);
                    return;
                }
                if (string.IsNullOrEmpty(encoded)) return;
                blob = encoded;
            }

            if (blob == _lastBlob) return;
            _lastBlob = blob;

            try
            {
                ModHelper.SetModData(AutoUvLampMod.ModId, ModDataKey, blob);
                AutoUvLampMod.Info(blob == EmptyBlob
                    ? "faucet slot: lamp removed, state saved"
                    : "faucet slot: lamp installed, state saved");
            }
            catch (Exception e)
            {
                AutoUvLampMod.Warning("faucet slot: save failed: " + e.Message);
            }
        }

        void LoadLamp()
        {            if (_slot == null) return;

            string blob = null;
            try { blob = ModHelper.GetModData(AutoUvLampMod.ModId, ModDataKey); }
            catch (Exception e)
            {
                AutoUvLampMod.Warning("faucet slot: read state failed: " + e.Message);
                return;
            }

            if (string.IsNullOrEmpty(blob) || blob == EmptyBlob)
            {
                _lastBlob = EmptyBlob;
                return;
            }

            _loading = true;
            try
            {
                GameItem item = Decode(blob);
                if (item == null)
                {
                    AutoUvLampMod.Warning("faucet slot: could not restore the installed lamp");
                    return;
                }

                GraphUtils.TryAcceptAll(_slot, item, -1);

                bool placed = false;
                var after = _slot.childItems;
                if (after != null)
                {
                    foreach (var x in after) { if (x == item) { placed = true; break; } }
                }

                if (!placed)
                {
                    try { item.Destroy(); } catch { }
                    AutoUvLampMod.Warning("faucet slot: slot refused the restored lamp (shape mismatch)");
                    return;
                }

                _lastBlob = blob;
                AutoUvLampMod.Info("faucet slot: installed lamp restored from save");
            }
            catch (Exception e)
            {
                AutoUvLampMod.Warning("faucet slot: restore failed: " + e.Message);
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>
        /// 用游戏自己的编码器序列化单件物品（与存档里物品同格式）。
        /// EncodeItem 会把 itemFeatures 追加进 PlayerStore.savedItemFeatureList —— 那是 SaveGame
        /// 专用的列表、存档开头会被整体重置，我们不在存档流程内，必须快照长度后截断，不能污染整档。
        /// </summary>
        static string Encode(GameItem item)
        {
            var store = PlayerStore.Instance;
            if (store == null) return null;

            int before = -1;
            try { before = store.savedItemFeatureList.Count; } catch { before = -1; }

            try
            {
                return store.EncodeItem(item);
            }
            finally
            {
                try
                {
                    if (before >= 0)
                    {
                        var list = store.savedItemFeatureList;
                        while (list.Count > before) list.RemoveAt(list.Count - 1);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 还原单件物品。只走 SaveManager.DecodeNodes，不用 PlayerStore.DecodeSaveItem ——
        /// 后者的契约是「存档袋」（根物品会被 Destroy、子物品吐进目标背包），单件做根会被销毁。
        /// </summary>
        static GameItem Decode(string blob)
        {
            SaveState state = null;
            try { state = JsonUtility.FromJson<SaveState>(blob); } catch { }
            if (state == null || state.saveItems == null || state.saveItems.Count == 0) return null;

            GameItem item = null;
            try { item = SaveManager.DecodeNodes(state.saveItems); } catch { }
            if (item == null) return null;

            // 读档后的原版修整（耐久 feature 与标签对齐），与 PlayerStore.DecodeSaveItem 的做法一致。
            try
            {
                int real = DurabilityHelper.GetRealDurability(item);
                int max = DurabilityHelper.GetMaxDurability(item);
                if (max > 0 && real >= 0 && real <= max)
                {
                    DurabilityHelper.SetDurability(item, real, max, DurabilityHelper.GetDefDurability(item));
                }
            }
            catch { }

            return item;
        }

        static int CurrentSlotId()
        {
            try
            {
                var store = PlayerStore.Instance;
                return store == null ? -1 : store.saveSlotId;
            }
            catch { return -1; }
        }
    }
}
