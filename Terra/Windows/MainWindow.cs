using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using System;
using System.Numerics;

namespace Terra.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly string goatImagePath;
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin, string goatImagePath)
        : base("Farming Manager##With a hidden ID", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 400), // Bumped minimum height slightly to fit the slider menu
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.goatImagePath = goatImagePath;
        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // --- 1. Control Panel ---
        ImGui.Text("Farming Control Panel");
        ImGui.Separator();

        // Water Button
        if (ImGui.Button("Water Bed", new Vector2(100, 25)))
        {
            plugin.WaterNearestBed();
        }

        ImGui.SameLine();

        // Harvest Button
        if (ImGui.Button("Harvest Bed", new Vector2(100, 25)))
        {
            plugin.HarvestNearestBed();
        }

        ImGui.SameLine();

        // Stop Button (Red)
        using (var color = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.6f, 0f, 0f, 1f)))
        {
            if (ImGui.Button("Stop", new Vector2(60, 25)))
            {
                plugin.wateringState = 0;
            }
        }


        ImGui.Spacing();

        // --- 2. Progress Bar ---
        float progress = 0f;
        int current = plugin.currentPlantIndex;
        int total = plugin.plantsToWater.Count;

        if (plugin.wateringState > 0 && total > 0)
        {
            progress = (float)current / total;
            string verb = plugin.isHarvesting ? "Harvesting" : "Tending";
            ImGui.Text($"Status: {verb} Plant {Math.Min(current + 1, total)} of {total}");
        }
        else
        {
            ImGui.Text("Status: Idle");
        }

        ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{Math.Round(progress * 100)}%");

        ImGuiHelpers.ScaledDummy(10.0f);

        // --- NEW: Advanced Settings Menu ---
        if (ImGui.CollapsingHeader("Advanced Timing Settings"))
        {
            // Bright Orange Warning Text
            ImGui.TextColored(new Vector4(1.0f, 0.65f, 0.0f, 1.0f), "WARNING: Lowering these too much will break the bot!");
            ImGui.Spacing();

            // Sliders locked to your requested limits
            ImGui.SliderInt("Water Menu Delay", ref plugin.waterMenuDelay, 10, 200, "%d ms");
            ImGui.SliderInt("General Anim Delay", ref plugin.generalDelay, 10, 500, "%d ms");
            ImGui.SliderInt("Between plants Anim Delay", ref plugin.betweenPlantsDelay, 500, 2500, "%d ms");
            ImGui.SliderInt("Harvest Anim Delay", ref plugin.harvestAnimDelay, 10, 3000, "%d ms");

            // Inside your ImGui window drawing method:
            if (ImGui.Checkbox("Show Debug Messages in Chat", ref plugin.Configuration.ShowDebugMessages))
            {
                plugin.Configuration.Save();
            }
        }

        ImGuiHelpers.ScaledDummy(10.0f);

        // --- 3. Character Details Child ---
        using (var child = ImRaii.Child("DetailsChild", Vector2.Zero, true))
        {
            if (child.Success)
            {
                ImGui.Text("Character Information:");

                var playerState = Plugin.PlayerState;
                if (!playerState.IsLoaded)
                {
                    ImGui.Text("Player not logged in.");
                    return;
                }

                // Job Icon and Level
                var jobIconId = 62100 + playerState.ClassJob.RowId;
                var iconTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(jobIconId)).GetWrapOrEmpty();
                ImGui.Image(iconTexture.Handle, new Vector2(24, 24) * ImGuiHelpers.GlobalScale);

                ImGui.SameLine();
                ImGui.Text($"{playerState.ClassJob.Value.Abbreviation} [Level {playerState.Level}]");

                // Location info
                var territoryId = Plugin.ClientState.TerritoryType;
                if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
                {
                    ImGui.Text($"Location: {territoryRow.PlaceName.Value.Name}");
                }

                ImGui.Separator();

                // Display the derp image
                var goatImage = Plugin.TextureProvider.GetFromFile(goatImagePath).GetWrapOrDefault();
                if (goatImage != null)
                {
                    ImGui.Image(goatImage.Handle, new Vector2(goatImage.Size.X * 0.08f, goatImage.Size.Y * 0.08f));
                }
            }
        }
    }
}
