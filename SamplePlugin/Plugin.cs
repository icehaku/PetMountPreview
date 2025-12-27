using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
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
    private ulong lastHoveredItem;

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
            lastHoveredItem = 0;
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

        var itemName = item.Name.ToString();
        Log.Information($"Hovered Item: {itemName} ({hovered})");

        var itemActionRef = item.ItemAction;
        if (!itemActionRef.IsValid)
            return;

        var itemAction = itemActionRef.Value;

        // 🐾 MINION
        if (itemAction.RowId == 853)
        {
            var minionId = itemAction.Data[0];
            var minion = DataManager
                .GetExcelSheet<Lumina.Excel.Sheets.Companion>()?
                .GetRow(minionId);

            Log.Information(
                $"MINION: {minion?.Singular ?? "Unknown"} (ID: {minionId})"
            );
            return;
        }

        // 🐎 MOUNT
        if (itemAction.RowId == 1322)
        {
            var mountId = itemAction.Data[0];
            var mount = DataManager
                .GetExcelSheet<Lumina.Excel.Sheets.Mount>()?
                .GetRow(mountId);

            Log.Information(
                $"MOUNT: {mount?.Singular ?? "Unknown"} (ID: {mountId})"
            );
            return;
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
