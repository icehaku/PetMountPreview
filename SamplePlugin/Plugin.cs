using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SamplePlugin.Windows;

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

    private const string CommandName = "/pmycommand";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SamplePlugin");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private PreviewWindow PreviewWindow { get; init; }
    private ulong lastHoveredItem;

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

        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var hovered = GameGui.HoveredItem;

        if (hovered == 0)
        {
            if (lastHoveredItem != 0)
            {
                PreviewWindow.HidePreview();
                lastHoveredItem = 0;
            }
            return;
        }

        if (hovered == lastHoveredItem)
            return;

        lastHoveredItem = hovered;

        var itemSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        if (itemSheet == null)
            return;

        if (!itemSheet.TryGetRow((uint)hovered, out var item))
            return;

        // 🧊 ADICIONE ESTA LINHA
        IceDebugger(hovered, item);

        var itemActionRef = item.ItemAction;
        if (!itemActionRef.IsValid)
        {
            PreviewWindow.HidePreview();
            return;
        }

        var itemAction = itemActionRef.Value;
        var actionRef = itemAction.Action;

        if (!actionRef.IsValid)
        {
            PreviewWindow.HidePreview();
            return;
        }

        var action = actionRef.Value;

        // 🐾 MINION (Action ID 853)
        if (action.RowId == 853)
        {
            var minionId = (uint)itemAction.Data[0];
            var companionSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Companion>();

            if (companionSheet != null && companionSheet.TryGetRow(minionId, out var companion))
            {
                var singularProp = companion.GetType().GetProperty("Singular");
                var nameProp = singularProp?.GetValue(companion);
                var name = nameProp?.ToString() ?? "Unknown Minion";

                var iconProp = companion.GetType().GetProperty("Icon");
                var iconValue = iconProp?.GetValue(companion);
                var iconId = iconValue != null ? Convert.ToUInt32(iconValue) : 0u;

                if (iconId > 0)
                {
                    // ADICIONA O OFFSET DE 64000 PARA MINIONS!
                    var hrIconId = iconId + 64000;
                    var folder = $"{(hrIconId / 1000) * 1000:D6}";
                    var paddedIcon = $"{hrIconId:D6}";
                    var hrPath = $"ui/icon/{folder}/{paddedIcon}_hr1.tex";

                    Log.Information($"Minion HR Path: {hrPath}");

                    var sharedTexture = TextureProvider.GetFromGame(hrPath);
                    PreviewWindow.ShowPreview(sharedTexture, $"🐾 {name}");
                    Log.Information($"Showing minion: {name}");
                }
            }
            return;
        }

        // 🐎 MOUNT (Action ID 1322)
        if (action.RowId == 1322)
        {
            var mountId = (uint)itemAction.Data[0];
            var mountSheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Mount>();

            if (mountSheet != null && mountSheet.TryGetRow(mountId, out var mount))
            {
                var singularProp = mount.GetType().GetProperty("Singular");
                var nameProp = singularProp?.GetValue(mount);
                var name = nameProp?.ToString() ?? "Unknown Mount";

                var iconProp = mount.GetType().GetProperty("Icon");
                var iconValue = iconProp?.GetValue(mount);
                var iconId = iconValue != null ? Convert.ToUInt32(iconValue) : 0u;

                if (iconId > 0)
                {
                    var hrIconId = iconId + 64000;
                    var folder = $"{(hrIconId / 1000) * 1000:D6}";
                    var paddedIcon = $"{hrIconId:D6}";
                    var hrPath = $"ui/icon/{folder}/{paddedIcon}_hr1.tex";

                    Log.Information($"Mount HR Path: {hrPath}");

                    var sharedTexture = TextureProvider.GetFromGame(hrPath);
                    PreviewWindow.ShowPreview(sharedTexture, $"🐎 {name}");
                    Log.Information($"Showing mount: {name}");
                }
            }
            return;
        }

        PreviewWindow.HidePreview();
    }

    private void IceDebugger(ulong itemId, Lumina.Excel.Sheets.Item item)
    {
        Log.Information($"=== ICE DEBUGGER - Item {itemId} ===");

        var itemIconProp = item.GetType().GetProperty("Icon");
        var itemIconId = Convert.ToUInt32(itemIconProp?.GetValue(item) ?? 0);

        Log.Information($"Item Icon: {itemIconId}");

        // Testa GetFromGameIcon com o Item Icon
        var sharedTexture = TextureProvider.GetFromGameIcon(itemIconId);
        var wrap = sharedTexture?.GetWrapOrDefault();

        if (wrap != null)
        {
            Log.Information($"GetFromGameIcon Result: {wrap.Width}x{wrap.Height}");
        }

        // Testa também com GameIconLookup
        var lookup = new Dalamud.Interface.Textures.GameIconLookup(itemIconId, hiRes: true);
        var sharedTextureHR = TextureProvider.GetFromGameIcon(lookup);
        var wrapHR = sharedTextureHR?.GetWrapOrDefault();

        if (wrapHR != null)
        {
            Log.Information($"GetFromGameIcon (HiRes) Result: {wrapHR.Width}x{wrapHR.Height}");
        }

        Log.Information("=== END ICE DEBUGGER ===");
    }


    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
