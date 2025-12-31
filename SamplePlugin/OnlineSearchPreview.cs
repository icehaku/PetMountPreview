using Dalamud.Plugin.Services;
using System;
using System.IO;

namespace SamplePlugin.Previews;

public unsafe class OnlineSearchPreview : IPreviewHandler
{
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;
    private readonly string pluginDirectory;
    private const string BaseUrl = "https://ffxiv.consolegameswiki.com/wiki/";

    // Array de categorias suportadas
    private static readonly uint[] SupportedCategories = new uint[]
    {
        73,  // Interior Wall
        76,  // Outdoor Furnishing
        79,  // Wall-mounted
        80,  // Rug 
        86,  // Triple Triad Card
        95,  // Painting
    };

    public OnlineSearchPreview(IDataManager dataManager, IPluginLog log, string pluginDirectory)
    {
        this.dataManager = dataManager;
        this.log = log;
        this.pluginDirectory = pluginDirectory;
    }

    public bool CanHandle(Lumina.Excel.Sheets.ItemAction itemAction)
    {
        return false; // Não usa ItemAction, apenas categoria
    }

    // 🔥 MODIFICADO: Agora verifica se categoria está no array
    public bool CanHandleByCategory(Lumina.Excel.Sheets.Item item)
    {
        var categoryId = item.ItemUICategory.RowId;
        
        // Verifica se a categoria está no array de suportadas
        foreach (var supportedCategoryId in SupportedCategories)
        {
            if (categoryId == supportedCategoryId)
            {
                log.Information($"OnlineSearchPreview handling item: {item.Name} (Category: {categoryId})");
                return true;
            }
        }
        
        return false;
    }

    public string GetImagePath(uint itemId, Lumina.Excel.Sheets.Item item)
    {
        // 🌐 PRIORIDADE 1: Tenta carregar de URL baseado no nome do item
        var itemName = item.Name.ToString();
        if (!string.IsNullOrEmpty(itemName))
        {
            // URL-encode do nome (Fool's Portal -> Fool%27s_Portal)
            var urlSafeName = Uri.EscapeDataString(itemName.Replace(" ", "_"));
            var wikiUrl = BaseUrl + urlSafeName;
            log.Information($"Wiki URL for item '{itemName}': {wikiUrl}");
            return $"WIKI:{wikiUrl}"; // Prefixo especial para identificar que é wiki
        }

        // 🖼️ PRIORIDADE 2: Tenta carregar PNG customizada local (por nome de categoria)
        var categoryName = GetCategoryFolderName(item.ItemUICategory.RowId);
        var customImagePath = Path.Combine(pluginDirectory, "images", categoryName, $"{itemId}.png");
        if (File.Exists(customImagePath))
        {
            log.Information($"Using custom image: {customImagePath}");
            return customImagePath;
        }

        // 🖼️ PRIORIDADE 3: PNG genérica local
        var genericImagePath = Path.Combine(pluginDirectory, "images", categoryName, "default.png");
        if (File.Exists(genericImagePath))
        {
            log.Information($"Using generic image: {genericImagePath}");
            return genericImagePath;
        }

        log.Information($"No custom image found for item: {itemId}");
        return string.Empty;
    }

    // Retorna nome da pasta baseado na categoria
    private string GetCategoryFolderName(uint categoryId)
    {
        return categoryId switch
        {
            73 => "interior-wall",
            76 => "outdoor-furnishing",
            79 => "wall-mounted",
            80 => "rug",
            86 => "triple-triad-card",
            95 => "painting",
            _ => "online-items" // Pasta genérica para categorias não mapeadas
        };
    }

    public (float width, float height, float scale) GetImageDimensions()
    {
        return (300f, 300f, 1.0f);
    }

    public void OnPreviewShow(uint itemId)
    {
        log.Information($"Showing online search preview: {itemId}");
    }

    public void OnPreviewHide()
    {
        // Nada a fazer
    }
}
