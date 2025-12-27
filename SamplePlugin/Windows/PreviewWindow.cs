using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace SamplePlugin.Windows;

public class PreviewWindow : Window
{
    private ISharedImmediateTexture? currentSharedTexture;
    private string currentName = string.Empty;

    public PreviewWindow() : base(
        "Companion Preview",
        ImGuiWindowFlags.NoDecoration |
        ImGuiWindowFlags.NoInputs |
        ImGuiWindowFlags.NoBackground |
        ImGuiWindowFlags.NoSavedSettings)
    {
        Size = new Vector2(300, 300);
        SizeCondition = ImGuiCond.Always;

        var viewport = ImGui.GetMainViewport();
        Position = new Vector2(
            viewport.Size.X - 320,
            viewport.Size.Y - 320
        );
        PositionCondition = ImGuiCond.Always;
    }

    public void ShowPreview(ISharedImmediateTexture sharedTexture, string name)
    {
        currentSharedTexture = sharedTexture;
        currentName = name;
        IsOpen = true;
    }

    public void HidePreview()
    {
        IsOpen = false;
    }

    public override void Draw()
    {
        if (currentSharedTexture == null)
            return;

        // Pega o wrap a cada frame para garantir que a textura está pronta
        var currentTexture = currentSharedTexture.GetWrapOrDefault();
        if (currentTexture == null)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();

        drawList.AddRectFilled(
            pos,
            pos + size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.8f)),
            10f
        );

        drawList.AddRect(
            pos,
            pos + size,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.3f)),
            10f,
            0,
            2f
        );

        var imageSize = new Vector2(256, 256);
        var imagePos = pos + new Vector2(
            (size.X - imageSize.X) / 2,
            20
        );

        ImGui.SetCursorScreenPos(imagePos);
        ImGui.Image(currentTexture.Handle, imageSize);

        var textSize = ImGui.CalcTextSize(currentName);
        var textPos = pos + new Vector2(
            (size.X - textSize.X) / 2,
            imagePos.Y + imageSize.Y + 10
        );

        ImGui.SetCursorScreenPos(textPos);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 1, 1));
        ImGui.Text(currentName);
        ImGui.PopStyleColor();
    }

    public override void OnClose()
    {
        currentSharedTexture = null;
        currentName = string.Empty;
    }
}
