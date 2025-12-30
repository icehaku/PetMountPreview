using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;

namespace SamplePlugin.Previews;

public unsafe class MountPreview : IPreviewHandler
{
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;

    private const uint MountActionId = 1322;
    private ushort? currentBgmId = null;

    public MountPreview(IDataManager dataManager, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

    public bool CanHandle(Lumina.Excel.Sheets.ItemAction itemAction)
    {
        var actionRef = itemAction.Action;
        if (!actionRef.IsValid) return false;

        var action = actionRef.Value;
        return action.RowId == MountActionId;
    }

    public bool CanHandleByCategory(Lumina.Excel.Sheets.Item item)
    {
        return false; // Minion e Mount usam ItemAction, não categoria
    }

    public string GetImagePath(uint mountId)
    {
        var mountSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Mount>();
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

    public (float width, float height, float scale) GetImageDimensions()
    {
        return (250f, 250f, 0.8f);
    }

    public void OnPreviewShow(uint mountId)
    {
        var bgmId = GetMountBGMId(mountId);
        if (bgmId.HasValue)
        {
            // Evita tocar a mesma música repetidamente
            if (currentBgmId == bgmId) return;

            BGMSystem.SetBGM(bgmId.Value, 0);
            currentBgmId = bgmId;
            log.Information($"Playing mount BGM: {bgmId.Value}");
        }
    }

    public void OnPreviewHide()
    {
        if (!currentBgmId.HasValue) return;

        try
        {
            BGMSystem.Instance()->ResetBGM(0);
            currentBgmId = null;
            log.Information("Stopped mount BGM preview");
        }
        catch (Exception ex)
        {
            log.Error($"Error stopping BGM: {ex.Message}");
        }
    }

    private ushort? GetMountBGMId(uint mountId)
    {
        var mountSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Mount>();
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
}
