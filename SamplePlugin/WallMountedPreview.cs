using Dalamud.Plugin.Services;
using System;
using System.IO;

namespace SamplePlugin.Previews;

public unsafe class WallMountedPreview : IPreviewHandler
{
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;
    private readonly string pluginDirectory;

    private const uint WallMountedCategoryId = 79;

    public WallMountedPreview(IDataManager dataManager, IPluginLog log, string pluginDirectory)
    {
        this.dataManager = dataManager;
        this.log = log;
        this.pluginDirectory = pluginDirectory;
    }

    public bool CanHandle(Lumina.Excel.Sheets.ItemAction itemAction)
    {
        // Wall-Mounted não usa ItemAction
        return false;
    }

    public bool CanHandleByCategory(Lumina.Excel.Sheets.Item item)
    {
        return item.ItemUICategory.RowId == WallMountedCategoryId;
    }

    public string GetImagePath(uint itemId)
    {
        // 🖼️ Tenta carregar PNG customizada da pasta wall-mounted
        var customImagePath = Path.Combine(pluginDirectory, "images", "wall-mounted", $"{itemId}.png");

        if (File.Exists(customImagePath))
        {
            log.Information($"Using custom wall-mounted image: {customImagePath}");
            return customImagePath;
        }

        // 🖼️ PNG genérica
        var genericImagePath = Path.Combine(pluginDirectory, "images", "wall-mounted", "default.png");

        if (File.Exists(genericImagePath))
        {
            log.Information($"Using generic wall-mounted image: {genericImagePath}");
            return genericImagePath;
        }

        log.Information($"No custom image found for wall-mounted item: {itemId}");
        return string.Empty;
    }

    public (float width, float height, float scale) GetImageDimensions()
    {
        return (300f, 300f, 1.0f);
    }

    public void OnPreviewShow(uint itemId)
    {
        log.Information($"Showing wall-mounted preview: {itemId}");
    }

    public void OnPreviewHide()
    {
        // Nada a fazer
    }
}
