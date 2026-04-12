using Windows.ApplicationModel.DataTransfer;

namespace SysSuite.Core
{
    /// <summary>Copia testo log negli appunti Windows (<see cref="DataPackage"/> + <see cref="Clipboard"/>).</summary>
    public static class LogClipboardHelper
    {
        public static void CopyToClipboard(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
                ToastHelper.SendSuccess("SysSuite One", "Log copiato negli appunti.");
            }
            catch
            {
                ToastHelper.Send("SysSuite One", "Impossibile copiare negli appunti.");
            }
        }
    }
}
