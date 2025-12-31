using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HtmlAgilityPack;
using SamplePlugin.Previews;
using SamplePlugin.Windows;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;

namespace SamplePlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    private const string CommandName = "/pmycommand";
    private const uint CustomImageNodeId = 99999;

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SamplePlugin");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private PreviewWindow PreviewWindow { get; init; }

    private string lastImagePath = string.Empty;
    private IPreviewHandler? currentHandler = null;
    private readonly Dictionary<string, ISharedImmediateTexture> externalTextureCache = new();
    private readonly Dictionary<string, string> wikiImageUrlCache = new();
    private readonly HttpClient httpClient = new HttpClient();

    private readonly List<IPreviewHandler> previewHandlers = new();

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);
        PreviewWindow = new PreviewWindow();

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(PreviewWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "A useful message to display in /xlhelp"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "ItemDetail", OnItemDetailUpdate);
        GameGui.HoveredItemChanged += OnHoveredItemChanged;

        RegisterPreviewHandlers();

        Log.Information($"Plugin loaded!");
    }

    private void RegisterPreviewHandlers()
    {
        var pluginDir = PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty;

        previewHandlers.Add(new MinionPreview(DataManager, Log));
        previewHandlers.Add(new MountPreview(DataManager, Log));
        previewHandlers.Add(new OnlineSearchPreview(DataManager, Log, pluginDir));

        Log.Information($"Registered {previewHandlers.Count} preview handlers");
    }

    private unsafe void OnHoveredItemChanged(object? sender, ulong itemId)
    {
        if (itemId == 0)
        {
            PreviewWindow?.HidePreview();
            currentHandler?.OnPreviewHide();
            currentHandler = null;
        }
    }

    public void Dispose()
    {
        currentHandler?.OnPreviewHide();
        PreviewWindow?.HidePreview();
        externalTextureCache.Clear();
        wikiImageUrlCache.Clear();
        httpClient?.Dispose();

        AddonLifecycle.UnregisterListener(OnItemDetailUpdate);
        GameGui.HoveredItemChanged -= OnHoveredItemChanged;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);

        CleanupImageNode();
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    private unsafe void OnItemDetailUpdate(AddonEvent type, AddonArgs args)
    {
        var atkUnitBase = (AtkUnitBase*)(nint)args.Addon;
        if (atkUnitBase == null) return;

        var imageNode = GetImageNode(atkUnitBase);
        if (imageNode != null)
        {
            imageNode->AtkResNode.NodeFlags &= ~NodeFlags.Visible;
        }

        var hoveredItem = GameGui.HoveredItem;
        if (hoveredItem == 0) return;

        var itemSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        if (itemSheet == null) return;
        if (!itemSheet.TryGetRow((uint)hoveredItem, out var item))
            return;


        var categoryId = item.ItemUICategory.RowId;
        Log.Information($"OnlineSearchPreview handling item: {item.Name} (Category: {categoryId})");

        IPreviewHandler? handler = null;
        uint itemId = 0;

        // Primeiro tenta por categoria (Wall-Mounted)
        handler = previewHandlers.FirstOrDefault(h => h.CanHandleByCategory(item));
        if (handler != null)
        {
            itemId = item.RowId;
        }
        else
        {
            // Tenta por ItemAction (Minion/Mount)
            var itemActionRef = item.ItemAction;
            if (!itemActionRef.IsValid) return;

            var itemAction = itemActionRef.Value;
            handler = previewHandlers.FirstOrDefault(h => h.CanHandle(itemAction));

            if (handler == null) return;

            itemId = (uint)itemAction.Data[0];
        }

        if (currentHandler != handler)
        {
            currentHandler?.OnPreviewHide();
            currentHandler = handler;
        }

        var imagePath = handler.GetImagePath(itemId, item);

        if (string.IsNullOrEmpty(imagePath))
        {
            PreviewWindow?.HidePreview();
            return;
        }

        var (imageWidth, imageHeight, scale) = handler.GetImageDimensions();
        handler.OnPreviewShow(itemId);

        // Se é imagem externa (.png/.jpg) ou URL da wiki
        if (imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            imagePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            imagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            imagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            imagePath.StartsWith("WIKI:", StringComparison.OrdinalIgnoreCase))
        {
            if (imagePath.StartsWith("WIKI:"))
            {
                var wikiPageUrl = imagePath.Substring(5);
                var imageUrl = ScrapeWikiImageUrl(wikiPageUrl);

                // Verifica se conseguiu extrair URL da wiki
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    LoadExternalTextureInWindow(imageUrl, atkUnitBase, item);
                }
                else
                {
                    // Exibe mensagem de erro quando não encontra imagem na wiki
                    Log.Warning($"Image URL not found on wiki page: {wikiPageUrl}");
                    var screenWidth = ImGuiHelpers.MainViewport.Size.X;
                    var errorPosition = new Vector2(screenWidth - 320 - 20, 20);
                    PreviewWindow?.ShowError("Image not found Online", errorPosition);
                }
            }
            else
            {
                LoadExternalTextureInWindow(imagePath, atkUnitBase, item);
            }
            return;
        }

        // Textura do jogo (.tex) - usa tooltip nativo
        PreviewWindow?.HidePreview();

        var insertNode = atkUnitBase->GetNodeById(2);
        if (insertNode == null) return;

        var anchorNode = atkUnitBase->GetNodeById(47);
        if (anchorNode == null) return;

        if (imageNode == null)
        {
            imageNode = CreateImageNode(atkUnitBase, insertNode);
            if (imageNode == null) return;
        }

        imageNode->AtkResNode.NodeFlags |= NodeFlags.Visible;

        if (imagePath != lastImagePath)
        {
            Log.Information($"Loading texture: {imagePath}");
            imageNode->LoadTexture(imagePath);
            lastImagePath = imagePath;
        }

        var width = (ushort)((atkUnitBase->RootNode->Width - 20f) * scale);
        var height = (ushort)(width * imageHeight / imageWidth);

        imageNode->AtkResNode.SetWidth(width);
        imageNode->AtkResNode.SetHeight(height);

        var x = atkUnitBase->RootNode->Width / 2f - width / 2f;
        var y = anchorNode->Y + anchorNode->GetHeight() + 8;
        imageNode->AtkResNode.SetPositionFloat(x, y);

        var newHeight = (ushort)(imageNode->AtkResNode.Y + height + 16);
        atkUnitBase->WindowNode->AtkResNode.SetHeight(newHeight);
        atkUnitBase->WindowNode->Component->UldManager.SearchNodeById(2)->SetHeight(newHeight);
        insertNode->SetPositionFloat(insertNode->X, newHeight - 20);
        atkUnitBase->RootNode->SetHeight(newHeight);

        var screenHeight = ImGuiHelpers.MainViewport.Size.Y;
        var tooltipY = atkUnitBase->Y;
        var tooltipBottom = tooltipY + newHeight;

        if (tooltipBottom > screenHeight)
        {
            var overflow = tooltipBottom - screenHeight;
            atkUnitBase->SetPosition((short)atkUnitBase->X, (short)(tooltipY - overflow - 10));
        }
    }

    private unsafe void LoadExternalTextureInWindow(string filePath, AtkUnitBase* atkUnitBase, Lumina.Excel.Sheets.Item item)

    {
        try
        {
            // Calcula posição do erro/imagem (canto superior direito)
            var screenWidth = ImGuiHelpers.MainViewport.Size.X;
            var errorPosition = new Vector2(screenWidth - 320 - 20, 20);

            if (!externalTextureCache.TryGetValue(filePath, out var sharedTexture))
            {
                if (filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    // Download para cache local (SÍNCRONO - causa freeze mas funciona)
                    var cachedPath = DownloadImageToCache(filePath, item.Name.ToString());

                    // Verifica se download falhou
                    if (string.IsNullOrEmpty(cachedPath))
                    {
                        Log.Warning($"Failed to download image from URL: {filePath}");
                        PreviewWindow?.ShowError("Image not found Online", errorPosition);
                        return;
                    }

                    sharedTexture = TextureProvider.GetFromFile(cachedPath);
                }
                else
                {
                    sharedTexture = TextureProvider.GetFromFile(filePath);
                }

                // Verifica se conseguiu criar a textura
                if (sharedTexture == null)
                {
                    Log.Warning($"Failed to create texture from file: {filePath}");
                    PreviewWindow?.ShowError("Image not found Online", errorPosition);
                    return;
                }

                externalTextureCache[filePath] = sharedTexture;
            }

            var wrap = sharedTexture.GetWrapOrDefault();
            if (wrap != null)
            {
                var padding = 20f;
                var imageSize = new Vector2(wrap.Width, wrap.Height);
                var windowSize = imageSize + new Vector2(padding, padding);

                var tooltipPos = new Vector2(screenWidth - windowSize.X - 20, 20);

                PreviewWindow?.ShowPreview(sharedTexture, tooltipPos, windowSize);
                Log.Information($"Showing texture: {filePath}");
            }
            else
            {
                var windowSize = new Vector2(320, 320);
                var tooltipPos = new Vector2(screenWidth - windowSize.X - 20, 20);

                PreviewWindow?.ShowPreview(sharedTexture, tooltipPos, windowSize);
            }
        }
        catch (Exception ex)
        {
            // 🔥 NOVO: Mostra erro quando ocorre exceção
            Log.Error($"Error loading external texture: {ex.Message}");
            var screenWidth = ImGuiHelpers.MainViewport.Size.X;
            var errorPosition = new Vector2(screenWidth - 320 - 20, 20);
            PreviewWindow?.ShowError("Image not found Online", errorPosition);
        }
    }

    private string DownloadImageToCache(string url, string itemName)
    {
        try
        {
            Log.Information($"Downloading image from URL: {url}");

            // Usa nome do item como nome do arquivo
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeName = string.Join("_", itemName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');

            if (safeName.Length > 200)
            {
                safeName = safeName.Substring(0, 200);
            }

            var fileName = $"{safeName}.png";
            var pluginDir = PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty;
            var cacheDir = Path.Combine(pluginDir, "cache", "images");
            var cachePath = Path.Combine(cacheDir, fileName);

            Directory.CreateDirectory(cacheDir);

            if (File.Exists(cachePath))
            {
                Log.Information($"Using cached image: {cachePath}"); 
                return cachePath;
            }

            var imageBytes = httpClient.GetByteArrayAsync(url).Result;
            File.WriteAllBytes(cachePath, imageBytes);

            Log.Information($"Downloaded to cache: {cachePath}");
            return cachePath;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to download image from URL: {ex.Message}");
            return string.Empty;
        }
    }

    private string ScrapeWikiImageUrl(string wikiPageUrl)
    {
        try
        {
            if (wikiImageUrlCache.TryGetValue(wikiPageUrl, out var cachedUrl))
            {
                Log.Information($"Using cached wiki image URL: {cachedUrl}");
                return cachedUrl;
            }

            Log.Information($"Scraping wiki page: {wikiPageUrl}");

            var html = httpClient.GetStringAsync(wikiPageUrl).Result;

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            var imageNode = htmlDoc.DocumentNode.SelectSingleNode("//p[@class='image_wrapper']//img");

            if (imageNode != null)
            {
                var src = imageNode.GetAttributeValue("src", "");

                if (!string.IsNullOrEmpty(src))
                {
                    string fullImageUrl;
                    if (src.StartsWith("http"))
                    {
                        fullImageUrl = src;
                    }
                    else if (src.StartsWith("/"))
                    {
                        fullImageUrl = "https://ffxiv.consolegameswiki.com" + src;
                    }
                    else
                    {
                        fullImageUrl = "https://ffxiv.consolegameswiki.com/" + src;
                    }

                    if (fullImageUrl.Contains("/thumb/"))
                    {
                        fullImageUrl = fullImageUrl.Replace("/thumb/", "/");

                        var extensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                        foreach (var ext in extensions)
                        {
                            var firstExtIndex = fullImageUrl.IndexOf(ext);
                            if (firstExtIndex > 0)
                            {
                                fullImageUrl = fullImageUrl.Substring(0, firstExtIndex + ext.Length);
                                break;
                            }
                        }
                    }

                    Log.Information($"Final wiki image URL: {fullImageUrl}");
                    wikiImageUrlCache[wikiPageUrl] = fullImageUrl;
                    return fullImageUrl;
                }
            }

            Log.Warning($"Could not find image in wiki page: {wikiPageUrl}");
            return string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error($"Error scraping wiki page: {ex.Message}");
            return string.Empty;
        }
    }

    private unsafe AtkImageNode* GetImageNode(AtkUnitBase* atkUnitBase)
    {
        if (atkUnitBase == null) return null;

        for (var i = 0; i < atkUnitBase->UldManager.NodeListCount; i++)
        {
            var node = atkUnitBase->UldManager.NodeList[i];
            if (node->NodeId == CustomImageNodeId && node->Type == NodeType.Image)
            {
                return (AtkImageNode*)node;
            }
        }

        return null;
    }

    private unsafe AtkImageNode* CreateImageNode(AtkUnitBase* atkUnitBase, AtkResNode* insertNode)
    {
        Log.Information("Creating image node");

        var imageNode = IMemorySpace.GetUISpace()->Create<AtkImageNode>();
        imageNode->AtkResNode.Type = NodeType.Image;
        imageNode->AtkResNode.NodeId = CustomImageNodeId;
        imageNode->AtkResNode.NodeFlags = NodeFlags.AnchorTop | NodeFlags.AnchorLeft | NodeFlags.Visible;
        imageNode->AtkResNode.DrawFlags = 0;
        imageNode->WrapMode = 1;
        imageNode->Flags = ImageNodeFlags.AutoFit;

        var partsList = (AtkUldPartsList*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldPartsList), 8);
        if (partsList == null)
        {
            Log.Error("Failed to alloc partsList");
            imageNode->AtkResNode.Destroy(true);
            return null;
        }

        partsList->Id = 0;
        partsList->PartCount = 1;

        var part = (AtkUldPart*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldPart), 8);
        if (part == null)
        {
            Log.Error("Failed to alloc part");
            IMemorySpace.Free(partsList, (ulong)sizeof(AtkUldPartsList));
            imageNode->AtkResNode.Destroy(true);
            return null;
        }

        part->U = 0;
        part->V = 0;
        part->Width = 256;
        part->Height = 256;

        partsList->Parts = part;

        var asset = (AtkUldAsset*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldAsset), 8);
        if (asset == null)
        {
            Log.Error("Failed to alloc asset");
            IMemorySpace.Free(part, (ulong)sizeof(AtkUldPart));
            IMemorySpace.Free(partsList, (ulong)sizeof(AtkUldPartsList));
            imageNode->AtkResNode.Destroy(true);
            return null;
        }

        asset->Id = 0;
        asset->AtkTexture.Ctor();
        part->UldAsset = asset;
        imageNode->PartsList = partsList;

        var prev = insertNode->PrevSiblingNode;
        imageNode->AtkResNode.ParentNode = insertNode->ParentNode;

        insertNode->PrevSiblingNode = (AtkResNode*)imageNode;

        if (prev != null) prev->NextSiblingNode = (AtkResNode*)imageNode;

        imageNode->AtkResNode.PrevSiblingNode = prev;
        imageNode->AtkResNode.NextSiblingNode = insertNode;

        atkUnitBase->UldManager.UpdateDrawNodeList();

        Log.Information("Image node created successfully");
        return imageNode;
    }

    private unsafe void CleanupImageNode()
    {
        var addonPtr = GameGui.GetAddonByName("ItemDetail");
        if (addonPtr == IntPtr.Zero) return;

        var unitBase = (AtkUnitBase*)(nint)addonPtr;
        var imageNode = GetImageNode(unitBase);
        if (imageNode == null) return;

        if (imageNode->AtkResNode.PrevSiblingNode != null)
            imageNode->AtkResNode.PrevSiblingNode->NextSiblingNode = imageNode->AtkResNode.NextSiblingNode;
        if (imageNode->AtkResNode.NextSiblingNode != null)
            imageNode->AtkResNode.NextSiblingNode->PrevSiblingNode = imageNode->AtkResNode.PrevSiblingNode;

        unitBase->UldManager.UpdateDrawNodeList();

        if (imageNode->PartsList != null)
        {
            if (imageNode->PartsList->Parts != null)
            {
                if (imageNode->PartsList->Parts->UldAsset != null)
                    IMemorySpace.Free(imageNode->PartsList->Parts->UldAsset, (ulong)sizeof(AtkUldAsset));
                IMemorySpace.Free(imageNode->PartsList->Parts, (ulong)sizeof(AtkUldPart));
            }
            IMemorySpace.Free(imageNode->PartsList, (ulong)sizeof(AtkUldPartsList));
        }

        imageNode->AtkResNode.Destroy(true);
        lastImagePath = string.Empty;
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
