using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace SamplePlugin.Windows;

public class PreviewWindow : Window
{
    private ISharedImmediateTexture? currentTexture;
    private string? errorMessage;
    private const float MaxImageSize = 400f; // 🔥 Constante compartilhada

    public PreviewWindow() : base(
        "Preview##PreviewWindow",
        ImGuiWindowFlags.NoDecoration |
        ImGuiWindowFlags.NoInputs |
        ImGuiWindowFlags.NoBackground |
        ImGuiWindowFlags.NoSavedSettings)
    {
        Size = new Vector2(200, 200);
        SizeCondition = ImGuiCond.Always;
        IsOpen = false;
    }

    // 🔥 NOVO: Calcula tamanho ideal ANTES de abrir a janela
    private Vector2 CalculateWindowSize(IDalamudTextureWrap wrap)
    {
        var padding = 20f;
        var imageSize = new Vector2(wrap.Width, wrap.Height);

        // Limita tamanho máximo
        if (imageSize.X > MaxImageSize || imageSize.Y > MaxImageSize)
        {
            var scale = Math.Min(MaxImageSize / imageSize.X, MaxImageSize / imageSize.Y);
            imageSize = new Vector2(imageSize.X * scale, imageSize.Y * scale);
        }

        return imageSize + new Vector2(padding, padding);
    }

    // 🔥 MODIFICADO: Tenta obter wrap e calcular tamanho antes de abrir
    public void ShowPreview(ISharedImmediateTexture texture, Vector2 position, Vector2 size)
    {
        currentTexture = texture;
        errorMessage = null;

        // 🔥 NOVO: Tenta obter wrap imediatamente
        var wrap = texture.GetWrapOrDefault();
        if (wrap != null)
        {
            // Se conseguiu o wrap, calcula tamanho correto
            var calculatedSize = CalculateWindowSize(wrap);
            Size = calculatedSize;

            // Recalcula posição com tamanho correto
            var screenWidth = ImGuiHelpers.MainViewport.Size.X;
            Position = new Vector2(screenWidth - calculatedSize.X - 20, 20);
        }
        else
        {
            // Se não tem wrap ainda, usa tamanho fornecido
            Size = size;
            Position = position;
        }

        SizeCondition = ImGuiCond.Always;
        PositionCondition = ImGuiCond.Always;
        IsOpen = true;
    }

    public void ShowError(string message, Vector2 position)
    {
        currentTexture = null;
        errorMessage = message;

        var textSize = ImGui.CalcTextSize(message);
        var padding = 40f;
        Size = textSize + new Vector2(padding * 2, padding * 2);
        Position = position;
        SizeCondition = ImGuiCond.Always;
        PositionCondition = ImGuiCond.Always;

        IsOpen = true;
    }

    public void HidePreview()
    {
        IsOpen = false;
        currentTexture = null;
        errorMessage = null;
    }

    public override void Draw()
    {
        // PRIORIDADE 1: Mensagem de erro
        if (errorMessage != null)
        {
            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var drawList = ImGui.GetWindowDrawList();

            drawList.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.85f)), 10f);
            drawList.AddRect(pos, pos + size, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.3f)), 10f, 0, 2f);

            var textSize = ImGui.CalcTextSize(errorMessage);
            var textPos = pos + (size - textSize) / 2;
            ImGui.SetCursorScreenPos(textPos);

            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), errorMessage);
            return;
        }

        // PRIORIDADE 2: Textura
        if (currentTexture == null) return;

        var wrap = currentTexture.GetWrapOrDefault();
        if (wrap == null)
        {
            // Loading state
            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.85f)), 10f);
            var loadingText = "Loading...";
            var textSize = ImGui.CalcTextSize(loadingText);
            ImGui.SetCursorScreenPos(pos + (size - textSize) / 2);
            ImGui.Text(loadingText);
            return;
        }

        // 🔥 SIMPLIFICADO: Calcula tamanho correto
        var windowSize = CalculateWindowSize(wrap);

        // 🔥 NOVO: Só recalcula se mudou (evita recálculo todo frame)
        var currentSize = Size ?? new Vector2(200, 200);
        if (Math.Abs(currentSize.X - windowSize.X) > 1f || Math.Abs(currentSize.Y - windowSize.Y) > 1f)
        {
            Size = windowSize;
            SizeCondition = ImGuiCond.Always;

            var screenWidth = ImGuiHelpers.MainViewport.Size.X;
            Position = new Vector2(screenWidth - windowSize.X - 20, 20);
            PositionCondition = ImGuiCond.Always;
        }

        var drawList2 = ImGui.GetWindowDrawList();
        var pos2 = ImGui.GetWindowPos();
        var size2 = ImGui.GetWindowSize();

        // Fundo preto semi-transparente
        drawList2.AddRectFilled(pos2, pos2 + size2, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.85f)), 10f);

        // Borda branca sutil
        drawList2.AddRect(pos2, pos2 + size2, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.3f)), 10f, 0, 2f);

        // Desenha a imagem com padding
        var imagePadding = 10f;
        var imagePos = pos2 + new Vector2(imagePadding, imagePadding);
        var finalImageSize = size2 - new Vector2(imagePadding * 2, imagePadding * 2);
        ImGui.SetCursorScreenPos(imagePos);
        ImGui.Image(wrap.Handle, finalImageSize);
    }
}
