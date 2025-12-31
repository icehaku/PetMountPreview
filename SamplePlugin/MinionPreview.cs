using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;

namespace SamplePlugin.Previews;

public unsafe class MinionPreview : IPreviewHandler
{
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;

    private const uint MinionActionId = 853;

    public MinionPreview(IDataManager dataManager, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

    public bool CanHandle(Lumina.Excel.Sheets.ItemAction itemAction)
    {
        var actionRef = itemAction.Action;
        if (!actionRef.IsValid) return false;

        var action = actionRef.Value;
        return action.RowId == MinionActionId;
    }

    public bool CanHandleByCategory(Lumina.Excel.Sheets.Item item)
    {
        return false; // Minion e Mount usam ItemAction, não categoria
    }

    public string GetImagePath(uint minionId, Lumina.Excel.Sheets.Item item)
    {
        var companionSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Companion>();
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

    public (float width, float height, float scale) GetImageDimensions()
    {
        return (100f, 100f, 0.8f);
    }

    public void OnPreviewShow(uint minionId)
    {
        // Minions não tocam música, então apenas loga
        log.Information($"Showing minion preview: {minionId}");
    }

    public void OnPreviewHide()
    {
        // Nada a fazer para minions
    }
}
