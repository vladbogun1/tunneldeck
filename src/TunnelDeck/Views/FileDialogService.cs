using Microsoft.Win32;

namespace TunnelDeck.Views;

/// <summary>
/// Native "pick an .exe" dialog. Sets a flag while open so the tray flyout does
/// not auto-hide when it loses focus to the OS file picker.
/// </summary>
public static class FileDialogService
{
    /// <summary>True while a native dialog is open; the flyout checks this before auto-hiding.</summary>
    public static bool DialogOpen { get; private set; }

    public static string? PickExecutable()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите приложение",
            Filter = "Программы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };

        DialogOpen = true;
        try
        {
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
        finally
        {
            DialogOpen = false;
        }
    }
}
