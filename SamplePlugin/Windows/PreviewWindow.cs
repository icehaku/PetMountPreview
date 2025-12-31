using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace SamplePlugin.Windows;

public class PreviewWindow : Window
{
    private ISharedImmediateTexture? currentTexture;
    private string? errorMessage; // 🔥 NOVO: Campo para mensagem de erro

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

    public void ShowPreview(ISharedImmediateTexture texture, Vector2 position, Vector2 size)
    {
        currentTexture = texture;
        errorMessage = null; // 🔥 Limpa erro ao mostrar textura
        Size = size;
        Position = position;
        IsOpen = true;
    }

    // 🔥 NOVO: Método para exibir mensagem de erro
    public void ShowError(string message, Vector2 position)
    {
        currentTexture = null;
        errorMessage = message;

        // Calcula tamanho baseado no texto
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
        errorMessage = null; // 🔥 Limpa erro ao esconder
    }

    public override void Draw()
    {
        // 🔥 PRIORIDADE 1: Mensagem de erro
        if (errorMessage != null)
        {
            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var drawList = ImGui.GetWindowDrawList();

            // Fundo preto semi-transparente
            drawList.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.85f)), 10f);

            // Borda branca sutil
            drawList.AddRect(pos, pos + size, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.3f)), 10f, 0, 2f);

            // Centraliza o texto
            var textSize = ImGui.CalcTextSize(errorMessage);
            var textPos = pos + (size - textSize) / 2;
            ImGui.SetCursorScreenPos(textPos);

            // Texto vermelho claro
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), errorMessage);
            return;
        }

        // 🔥 PRIORIDADE 2: Textura (com loading)
        if (currentTexture == null) return;

        var wrap = currentTexture.GetWrapOrDefault();
        if (wrap == null)
        {
            // Textura ainda não pronta, mostra loading
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

        // 🔥 Limita tamanho máximo da imagem
        var maxSize = 400f;
        var padding = 20f;
        var originalSize = new Vector2(wrap.Width, wrap.Height);
        var imageSize = originalSize;

        // Se a imagem for maior que o máximo, redimensiona mantendo proporção
        if (imageSize.X > maxSize || imageSize.Y > maxSize)
        {
            var scale = Math.Min(maxSize / imageSize.X, maxSize / imageSize.Y);
            imageSize = new Vector2(imageSize.X * scale, imageSize.Y * scale);
        }

        var windowSize = imageSize + new Vector2(padding, padding);
        if (Size != windowSize)
        {
            Size = windowSize;
            SizeCondition = ImGuiCond.Always;
            // Recalcula posição quando tamanho mudar
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
