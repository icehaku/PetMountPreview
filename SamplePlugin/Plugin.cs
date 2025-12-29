using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SamplePlugin.Windows;
using System;
using System.IO;

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

        Log.Information($"Plugin loaded!");
    }

    private unsafe void OnHoveredItemChanged(object? sender, ulong itemId)
    {
        if (itemId == 0)
        {
            //Log.Information("Mouse saiu de cima do item");
            BGMSystem.Instance()->ResetBGM(0);
            return;
        }

        //Log.Information($"Mouse está sobre o item: {itemId}");

        // Aqui você pode processar o item
        var itemSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        if (itemSheet != null && itemSheet.TryGetRow((uint)itemId, out var item))
        {
            Log.Information($"Item name: {item.Name}");
        }
    }

    public void Dispose()
    {
        AddonLifecycle.UnregisterListener(OnItemDetailUpdate);

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);

        GameGui.HoveredItemChanged -= OnHoveredItemChanged;

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
        //Log.Information($"hoveredItem: {hoveredItem}");
        if (hoveredItem == 0)
            return;

        var itemSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        if (itemSheet == null) return;

        if (!itemSheet.TryGetRow((uint)hoveredItem, out var item))
            return;

        var itemActionRef = item.ItemAction;
        if (!itemActionRef.IsValid)
            return;

        var itemAction = itemActionRef.Value;
        var actionRef = itemAction.Action;

        if (!actionRef.IsValid)
            return;

        var action = actionRef.Value;

        string imagePath = string.Empty;
        float imageWidth = 100;
        float imageHeight = 100;
        float scale = 0.8f;

        // 🐾 MINION (Action ID 853)
        if (action.RowId == 853)
        {
            var minionId = (uint)itemAction.Data[0];
            imagePath = GetMinionImagePath(minionId);
            imageWidth = 100;
            imageHeight = 100;
            scale = 0.8f;
        }
        // 🐎 MOUNT (Action ID 1322)
        else if (action.RowId == 1322)
        {
            /// 0 = Event<br/>
            /// 1 = Battle<br/>
            /// 2 = MiniGame (RhythmAction, TurnBreak)<br/>
            /// 3 = Content<br/>
            /// 4 = GFate<br/>
            /// 5 = Duel<br/>
            /// 6 = Mount<br/>
            /// 7 = Unknown, no xrefs<br/>
            /// 8 = Unknown, via packet (near PlayerState stuff)<br/>
            /// 9 = Wedding<br/>
            /// 10 = Town<br/>
            /// 11 = Territory

            var mountId = (uint)itemAction.Data[0];
            var bgmId = GetMountBGMId(mountId);
            if (bgmId.HasValue)
            {
                BGMSystem.SetBGM(bgmId.Value, 0);
            }

            imagePath = GetMountImagePath(mountId);
            imageWidth = 250;
            imageHeight = 250;
            scale = 0.8f;
        }

        if (string.IsNullOrEmpty(imagePath)) return;


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
        var height = (ushort)(width * imageWidth / imageHeight);

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

        // 🔧 ADICIONE ESTE CÓDIGO PARA EVITAR QUE SAIA DA TELA
        var screenHeight = ImGuiHelpers.MainViewport.Size.Y;
        var tooltipY = atkUnitBase->Y;
        var tooltipBottom = tooltipY + newHeight;

        // Se o tooltip sair da parte de baixo da tela, move para cima
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
        //imageNode->AtkResNode.NodeFlags = NodeFlags.AnchorTop | NodeFlags.AnchorLeft;
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

    private string GetMountImagePath(uint mountId)
    {
        var mountSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Mount>();
        if (mountSheet == null) return string.Empty;

        if (!mountSheet.TryGetRow(mountId, out var mount))
            return string.Empty;

        var iconProp = mount.GetType().GetProperty("Icon");
        var iconValue = iconProp?.GetValue(mount);
        var iconId = iconValue != null ? Convert.ToUInt32(iconValue) + 64000 : 0u;

        if (iconId == 0) return string.Empty;

        var folder = (iconId / 1000 * 1000).ToString("D6");
        var icon = iconId.ToString("D6");
        return $"ui/icon/{folder}/{icon}_hr1.tex";
    }

    private string GetMinionImagePath(uint minionId)
    {
        var companionSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Companion>();
        if (companionSheet == null) return string.Empty;

        if (!companionSheet.TryGetRow(minionId, out var companion))
            return string.Empty;

        var iconProp = companion.GetType().GetProperty("Icon");
        var iconValue = iconProp?.GetValue(companion);
        var iconId = iconValue != null ? Convert.ToUInt32(iconValue) + 64000 : 0u;

        if (iconId == 0) return string.Empty;

        var folder = (iconId / 1000 * 1000).ToString("D6");
        var icon = iconId.ToString("D6");
        return $"ui/icon/{folder}/{icon}_hr1.tex";
    }

    private ushort? GetMountBGMId(uint mountId)
    {
        var mountSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Mount>();
        if (mountSheet == null) return null;

        if (!mountSheet.TryGetRow(mountId, out var mount))
            return null;

        var rideBgmProp = mount.GetType().GetProperty("RideBGM");
        if (rideBgmProp == null) return null;

        var rideBgmRef = rideBgmProp.GetValue(mount);
        var isValidProp = rideBgmRef?.GetType().GetProperty("IsValid");
        var isValid = (bool)(isValidProp?.GetValue(rideBgmRef) ?? false);

        if (!isValid) return null;

        var valueProp = rideBgmRef?.GetType().GetProperty("Value");
        var bgm = valueProp?.GetValue(rideBgmRef);

        if (bgm == null) return null;

        var rowIdProp = bgm.GetType().GetProperty("RowId");
        var rowId = (uint)(rowIdProp?.GetValue(bgm) ?? 0);

        return rowId > 0 ? (ushort)rowId : null;
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
