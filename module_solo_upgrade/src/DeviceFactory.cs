using System;
using UnityEngine;

namespace ModuleSoloUpgrade
{
    // -------------------------------------------------------------------------
    //  Registers the device factory in PreBuiltItemHelper.preBuiltItems, the
    //  dictionary that ItemSpawner.Spawn / ModHelper.SpawnItem consult first.
    //  Idempotent, because the game may rebuild that dictionary on save load.
    // -------------------------------------------------------------------------
    public static class ItemRegistration
    {
        public static void Ensure()
        {
            try
            {
                var table = PreBuiltItemHelper.preBuiltItems;
                if (table == null)
                {
                    ModEntry.Log.Warning("preBuiltItems is null; factory not registered yet.");
                    return;
                }
                if (!table.ContainsKey(ModEntry.DeviceId))
                    table[ModEntry.DeviceId] = DeviceFactory.Create;
            }
            catch (Exception ex)
            {
                ModEntry.Log.Error("Item registration failed: " + ex);
            }
        }
    }

    // -------------------------------------------------------------------------
    //  Builds the device. The window layout mirrors
    //  MachineBottlePrinter.CreateMachineInventoryWindow so that the machinery
    //  helpers can find the slots by body index:
    //      body 0 -> MachineHelper.GetBatterySlot()   (PowerPanel)
    //      body 1 -> MachineHelper.GetModules()       (module bay panel)
    //  That index order is not cosmetic: both helpers read it from the window's
    //  MachineryPanelRow, so panel 0 must stay the power panel.
    // -------------------------------------------------------------------------
    public static class DeviceFactory
    {
        public static GameItem Create()
        {
            var batterySlot = new GameSlotInventory();
            // 3x3 module bay: nine cells, enough for any vanilla module shape
            // while keeping the window compact.
            var moduleGrid = new GameGridInventory(3, 3);

            // Callbacks are wired after the machine exists, exactly like vanilla
            // does (ctor callback is null, SetCallback later).
            var buttonImplant = new MachineryButton("植入自学习", null, -1, -1);
            var buttonLearn = new MachineryButton("自学习 +1", null, -1, -1);
            buttonImplant.SetDescription("为模块植入「自学习」词条（消耗电量）。");
            buttonLearn.SetDescription("推进一次自学习进度（消耗电量）。");

            var controlStack = MachineryStack.Vertical(new PixelElement[] { buttonImplant, buttonLearn });

            var panels = new MachineryPanel[3];
            panels[0] = MachineHelper.PowerPanel(batterySlot);
            panels[1] = MachineHelper.Panel("模块", moduleGrid, "output", true);
            panels[2] = MachineHelper.Panel("操作", controlStack, "control", true);

            // canMove must be true, exactly like the bottle printer's
            // isDraggable. PixelWindow.Show() early-returns when it is false,
            // skipping BringWindowsToFront, EnsureFloatingDock and the position
            // write — the window then sits under the main panel and cannot be
            // dragged out. The field is readonly, so the ctor is the only way in.
            var window = MachineHelper.CreateMachineryWindow(true, panels);

            var machine = ItemDirectory.CreateEmptyItem(ModEntry.DeviceId);
            machine.SetContentWindow(window);
            ApplySprite(machine);
            machine.name = ModEntry.DeviceName;
            machine.flavorText = ModEntry.DeviceFlavor;
            // Drives the Doctor's price: PlayerStore.BuyItem subtracts
            // GameItem.GetNegociatedValue(), which starts from unitValue.
            machine.unitValue = ModEntry.ShopPrice;

            // Not cosmetic. GraphUtils.TryAcceptAll(storage, item, -1) resolves
            // -1 to item.unitCount and then bails out with 0 when that is <= 0,
            // so an item built without SetAmount(1) can never be placed anywhere
            // — it just vanishes, with no error.
            machine.SetAmount(1);

            MachineHelper.SetupBatterySlot(batterySlot, machine);

            // The vanilla module bay: restricts the grid to module items, wires the
            // add/remove callbacks and makes MachineHelper.GetModules() work.
            // Passing null for onChanged is what MachineHydroponic does.
            MachineHelper.SetupModuleBay(moduleGrid, machine, null,
                new[] { ModuleHelper.MODULE_TYPE_UNIVERSAL });

            // Same tags the bottle printer carries; use the real constants so a
            // rename breaks the build instead of silently changing behaviour.
            machine.EnableTag(GeneralHelper.IMPORTANT_TAG, false);
            machine.EnableTag(ContainerHelper.CONTAINER_TAG, false);

            // Our own marker: lets the save-rebuild path and the tooltip recognise
            // the device even if its identifier was replaced.
            machine.ModifyTag(ModEntry.TagDevice, ts => ts.Enable(), true);
            machine.SyncModifiedState();

            // Buttons ---------------------------------------------------------
            buttonImplant.SetCallback(() => DeviceLogic.Implant(machine));
            buttonLearn.SetCallback(() => DeviceLogic.Learn(machine));

            // Let the UI layer keep the captions and tooltips showing the real
            // cost of the next press (it is not a constant, see DeviceUi).
            DeviceUi.Register(machine, buttonImplant, buttonLearn);

            // Official post-load hook: PlayerStore.DecodeSaveItem invokes
            // GameItemFunc.onLoaded for every item it rebuilds. Re-stamp the
            // marker here so a device restored from an older save still counts.
            machine.onLoaded = OnLoaded;

            AudioHelper.InitSmallMachine(machine);
            return machine;
        }

        // A shipped Textures/Items/self_learn_programmer.png (48x48) replaces the
        // sprite AND the grid shape, which is why the README pins the size. When
        // the file is absent we fall back to the vanilla bottle-printer art.
        private static void ApplySprite(GameItem machine)
        {
            try
            {
                var mods = ModLoader.Mods;
                if (mods != null)
                {
                    var owner = mods.Find(m => m != null && m.Manifest != null
                                               && m.Manifest.ID == ModEntry.ModId
                                               && m.IsEnabled);
                    if (owner != null && owner.Sprites != null
                        && owner.Sprites.ContainsKey(ModEntry.SpriteKey))
                    {
                        machine.SetSpriteAndShapeFromMod(ModEntry.ModId, ModEntry.SpriteKey);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntry.Log.Warning("Custom sprite lookup failed, using vanilla art: " + ex);
            }
            machine.SetSpriteAndShape("Items/items_tool", "bottle_printer");
        }

        private static void OnLoaded(GameItem item)
        {
            if (item == null) return;
            try
            {
                item.ModifyTag(ModEntry.TagDevice, ts => ts.Enable(), true);
                item.SyncModifiedState();
            }
            catch (Exception ex)
            {
                ModEntry.Log.Error("Device restore hook failed: " + ex);
            }
        }
    }
}
