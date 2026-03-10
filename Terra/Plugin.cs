using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Terra.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Terra;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private const string CommandName = "/frm";

    public List<IGameObject> plantsToWater = new();
    public int currentPlantIndex = 0;
    private long nextActionTime = 0;
    public int wateringState = 0;
    public bool isHarvesting = false;
    private int patchSessionCounter = 1;
    public bool waitingForUser = false;


    // UI Sliders
    public int waterMenuDelay = 300;
    public int harvestAnimDelay = 1500;
    public int generalDelay = 300;
    public int betweenPlantsDelay = 1000;
    public long uiNukeTimer = 0;

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("FarmingWay");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "Derp.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Type '/frm water' or '/frm harvest' to tend the nearest garden bed."
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        Framework.Update -= OnFrameworkUpdate;
    }

    private void OnCommand(string command, string args)
    {
        string subCommand = args.Trim().ToLower();

        if (subCommand == "water")
        {
            WaterNearestBed();
        }
        else if (subCommand == "harvest")
        {
            HarvestNearestBed();
        }
        else if (subCommand == "stop")
        {
            wateringState = 0;
            waitingForUser = false;
            ChatGui.Print("Emergency Stop Activated! Bot loop disabled.");
        }
        // --- NEW: The Step Command ---
        else if (subCommand == "yes" || subCommand == "y")
        {
            if (waitingForUser)
            {
                waitingForUser = false;
                ChatGui.Print("[FarmingBot] Resuming...");
            }
            else
            {
                ChatGui.Print("[FarmingBot] Bot is not currently paused.");
            }
        }
        else if (subCommand == "debug")
        {
            if (ClientState.LocalPlayer == null) return;
            ChatGui.Print("Scanning for Event Objects (plants/furniture) within 5 yalms...");

            foreach (var obj in ObjectTable)
            {
                if (obj.ObjectKind == ObjectKind.EventObj)
                {
                    float dist = Vector3.Distance(ClientState.LocalPlayer.Position, obj.Position);
                    if (dist < 5.0f)
                    {
                        ChatGui.Print($"Name: {obj.Name} | DataID: {obj.DataId} | Dist: {dist:F2}");
                    }
                }
            }
        }
        else if (subCommand == "ui" || subCommand == "")
        {
            MainWindow.Toggle();
        }
    }

    public void PrintDebug(string message)
    {
        if (Configuration.ShowDebugMessages)
        {
            ChatGui.Print(message);
        }
    }

    public void WaterNearestBed()
    {
        if (ClientState.LocalPlayer == null) return;
        List<IGameObject> plantsInThisBed = new List<IGameObject>();

        foreach (var obj in ObjectTable)
        {
            if (obj.ObjectKind == ObjectKind.EventObj && obj.DataId == 2003757)
            {
                float dist = Vector3.Distance(ClientState.LocalPlayer.Position, obj.Position);
                if (dist < 2.0f) plantsInThisBed.Add(obj);
            }
        }

        if (plantsInThisBed.Count == 0) return;

        plantsToWater = plantsInThisBed;
        currentPlantIndex = 0;
        isHarvesting = false;
        wateringState = 1;
        nextActionTime = Environment.TickCount64;

    }

    public void HarvestNearestBed()
    {
        if (ClientState.LocalPlayer == null) return;
        List<IGameObject> plantsInThisBed = new List<IGameObject>();

        foreach (var obj in ObjectTable)
        {
            if (obj.ObjectKind == ObjectKind.EventObj && obj.DataId == 2003757)
            {
                float dist = Vector3.Distance(ClientState.LocalPlayer.Position, obj.Position);
                if (dist < 2.0f) plantsInThisBed.Add(obj);
            }
        }

        if (plantsInThisBed.Count == 0) return;

        plantsToWater = plantsInThisBed;
        currentPlantIndex = 0;
        isHarvesting = true;
        wateringState = 1;
        nextActionTime = Environment.TickCount64;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (wateringState == 0) return;
        if (waitingForUser) return;
        if (Environment.TickCount64 < nextActionTime) return;

        // ------------------------------------------------------------------
        // STATE 1: Target and Interact / Safe Shutdown
        // ------------------------------------------------------------------
        if (wateringState == 1)
        {
            if (currentPlantIndex >= plantsToWater.Count)
            {
                wateringState = 0;
                TargetManager.Target = null;
                ChatGui.Print($"[FarmingBot] {(isHarvesting ? "Harvest" : "Watering")} sequence complete!");
                return;
            }

            var plant = plantsToWater[currentPlantIndex];
            TargetManager.Target = plant;

            unsafe
            {
                TargetSystem.Instance()->InteractWithObject(
                    (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)plant.Address, false);
            }

            PrintDebug($"[DEBUG] Interacting with Plant {currentPlantIndex + 1}...");
            wateringState = 2;
            nextActionTime = Environment.TickCount64 + generalDelay;
            return;
        }

        // ------------------------------------------------------------------
        // STATE 2: The Black Dialogue Popup (Talk)
        // ------------------------------------------------------------------
        else if (wateringState == 2)
        {
            unsafe
            {
                var talkWrapper = GameGui.GetAddonByName("Talk", 1);
                if (talkWrapper.Address != IntPtr.Zero)
                {
                    var talkAddon = (AddonTalk*)talkWrapper.Address;
                    if (talkAddon->AtkUnitBase.IsVisible)
                    {
                        AtkValue* talkValues = stackalloc AtkValue[1];
                        talkValues[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
                        talkValues[0].Int = 0;
                        talkAddon->AtkUnitBase.FireCallback(0, talkValues);

                        // Release the mouse lock
                        talkAddon->AtkUnitBase.Close(true);

                        PrintDebug("[DEBUG] Dismissing Dialogue. Waiting for Menu...");
                        wateringState = 3;
                        nextActionTime = Environment.TickCount64 + waterMenuDelay;
                        return;
                    }
                }
            }
            nextActionTime = Environment.TickCount64 + 100;
        }

        // ------------------------------------------------------------------
        // STATE 3: The Menu (SelectString)
        // ------------------------------------------------------------------
        else if (wateringState == 3)
        {
            unsafe
            {
                var selectWrapper = GameGui.GetAddonByName("SelectString", 1);
                if (selectWrapper.Address != IntPtr.Zero)
                {
                    var addon = (AddonSelectString*)selectWrapper.Address;
                    if (addon->AtkUnitBase.IsVisible)
                    {
                        AtkValue* values = stackalloc AtkValue[1];
                        values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;

                        // DYNAMIC INDEX: 
                        // If Harvesting: Click "Harvest Crop" (0)
                        // If Watering: Click "Tend Crop" (1)
                        values[0].Int = isHarvesting ? 0 : 1;

                        addon->AtkUnitBase.FireCallback(1, values);
                        addon->AtkUnitBase.Close(true);

                        PrintDebug($"[DEBUG] {(isHarvesting ? "Harvesting" : "Tending")} Crop...");

                        wateringState = 4;
                        // Use appropriate animation delay
                        nextActionTime = Environment.TickCount64 + (isHarvesting ? harvestAnimDelay : 2500);
                        return;
                    }
                }
            }
            nextActionTime = Environment.TickCount64 + 100;
        }

        // ------------------------------------------------------------------
        // STATE 4: Advance
        // ------------------------------------------------------------------
        else if (wateringState == 4)
        {
            currentPlantIndex++;
            wateringState = 1;
            nextActionTime = Environment.TickCount64 + 100;
        }
    }

    // This helper is now safer to keep in the code, though we aren't using 
    // it in the shutdown block anymore to prevent the crash.
    private unsafe void ForceCloseAddon(string name)
    {
        var ptr = GameGui.GetAddonByName(name, 1);
        if (ptr.Address != IntPtr.Zero)
        {
            var addon = (AtkUnitBase*)ptr.Address;
            if (addon->IsVisible)
            {
                addon->Close(true);
            }
        }
    }

    // Helper to nuke the UI at the end
    private unsafe void CleanupUI()
    {
        TargetManager.Target = null;
        var selectWrapper = GameGui.GetAddonByName("SelectString", 1);
        if (selectWrapper.Address != IntPtr.Zero)
        {
            var addon = (AddonSelectString*)selectWrapper.Address;
            AtkValue* values = stackalloc AtkValue[1];
            values[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
            values[0].Int = -1;
            addon->AtkUnitBase.FireCallback(unchecked((uint)-1), values);
        }
    }
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
