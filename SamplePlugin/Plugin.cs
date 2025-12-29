using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SamplePlugin.Previews;
using SamplePlugin.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    private string lastImagePath = string.Empty;
    private IPreviewHandler? currentHandler = null;

    // Lista de handlers registrados
    private readonly List<IPreviewHandler> previewHandlers = new();

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "A useful message to display in /xlhelp"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "ItemDetail", OnItemDetailUpdate);
        GameGui.HoveredItemChanged += OnHoveredItemChanged;

        // 🎯 Registra os handlers de preview
        RegisterPreviewHandlers();

        Log.Information($"Plugin loaded!");
    }

    private void RegisterPreviewHandlers()
    {
        previewHandlers.Add(new MinionPreview(DataManager, Log));
        previewHandlers.Add(new MountPreview(DataManager, Log));
        
        Log.Information($"Registered {previewHandlers.Count} preview handlers");
    }

    private unsafe void OnHoveredItemChanged(object? sender, ulong itemId)
    {
        if (itemId == 0)
        {
            // Notifica o handler atual para limpar
            currentHandler?.OnPreviewHide();
            currentHandler = null;
        }
    }

    public void Dispose()
    {
        // Limpa preview atual
        currentHandler?.OnPreviewHide();

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

        // Primeiro, esconde o node se existir
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

        var itemActionRef = item.ItemAction;
        if (!itemActionRef.IsValid)
            return;

        var itemAction = itemActionRef.Value;

        // 🎯 Encontra o handler apropriado para este item
        var handler = previewHandlers.FirstOrDefault(h => h.CanHandle(itemAction));
        if (handler == null) return;

        // Atualiza handler atual
        if (currentHandler != handler)
        {
            currentHandler?.OnPreviewHide();
            currentHandler = handler;
        }

        var itemId = (uint)itemAction.Data[0];
        var imagePath = handler.GetImagePath(itemId);
        
        if (string.IsNullOrEmpty(imagePath)) return;

        // Obtém dimensões do handler
        var (imageWidth, imageHeight, scale) = handler.GetImageDimensions();

        // Executa ações do handler (ex: tocar música)
        handler.OnPreviewShow(itemId);

        var insertNode = atkUnitBase->GetNodeById(2);
        if (insertNode == null) return;

        var anchorNode = atkUnitBase->GetNodeById(47);
        if (anchorNode == null) return;

        // Cria o node se não existir
        if (imageNode == null)
        {
            imageNode = CreateImageNode(atkUnitBase, insertNode);
            if (imageNode == null) return;
        }

        // Torna visível
        imageNode->AtkResNode.NodeFlags |= NodeFlags.Visible;

        // Carrega a textura se mudou
        if (imagePath != lastImagePath)
        {
            Log.Information($"Loading texture: {imagePath}");
            imageNode->LoadTexture(imagePath);
            lastImagePath = imagePath;
        }

        // Ajusta tamanho e posição
        var width = (ushort)((atkUnitBase->RootNode->Width - 20f) * scale);
        var height = (ushort)(width * imageHeight / imageWidth);

        imageNode->AtkResNode.SetWidth(width);
        imageNode->AtkResNode.SetHeight(height);

        var x = atkUnitBase->RootNode->Width / 2f - width / 2f;
        var y = anchorNode->Y + anchorNode->GetHeight() + 8;
        imageNode->AtkResNode.SetPositionFloat(x, y);

        // Ajusta altura do tooltip
        var newHeight = (ushort)(imageNode->AtkResNode.Y + height + 16);
        atkUnitBase->WindowNode->AtkResNode.SetHeight(newHeight);
        atkUnitBase->WindowNode->Component->UldManager.SearchNodeById(2)->SetHeight(newHeight);
        insertNode->SetPositionFloat(insertNode->X, newHeight - 20);
        atkUnitBase->RootNode->SetHeight(newHeight);

        // 🔧 Evita que o tooltip saia da tela
        var screenHeight = ImGuiHelpers.MainViewport.Size.Y;
        var tooltipY = atkUnitBase->Y;
        var tooltipBottom = tooltipY + newHeight;

        if (tooltipBottom > screenHeight)
        {
            var overflow = tooltipBottom - screenHeight;
            atkUnitBase->SetPosition((short)atkUnitBase->X, (short)(tooltipY - overflow - 10));
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

        // Cria PartsList
        var partsList = (AtkUldPartsList*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkUldPartsList), 8);
        if (partsList == null)
        {
            Log.Error("Failed to alloc partsList");
            imageNode->AtkResNode.Destroy(true);
            return null;
        }

        partsList->Id = 0;
        partsList->PartCount = 1;

        // Cria Part
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

        // Cria Asset
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

        // Adiciona à lista de nodes
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