namespace SamplePlugin.Previews;

public interface IPreviewHandler
{
    /// <summary>
    /// Verifica se este handler pode processar o item por ItemAction
    /// </summary>
    bool CanHandle(Lumina.Excel.Sheets.ItemAction itemAction);

    /// <summary>
    /// Verifica se este handler pode processar o item por categoria
    /// </summary>
    bool CanHandleByCategory(Lumina.Excel.Sheets.Item item);

    /// <summary>
    /// Obtém o caminho da imagem para o item
    /// </summary>
    string GetImagePath(uint itemId);

    /// <summary>
    /// Obtém as dimensões da imagem (width, height, scale)
    /// </summary>
    (float width, float height, float scale) GetImageDimensions();

    /// <summary>
    /// Executa ações adicionais ao exibir o preview (ex: tocar música)
    /// </summary>
    void OnPreviewShow(uint itemId);

    /// <summary>
    /// Executa ações ao esconder o preview (ex: parar música)
    /// </summary>
    void OnPreviewHide();
}
