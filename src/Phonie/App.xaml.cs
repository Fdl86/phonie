using System.Windows;
using Phonie.Services;

namespace Phonie;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            AppPaths.EnsurePortableStorage();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"PHONIE ne peut pas écrire dans son propre dossier.\n\n" +
                $"Dossier : {AppPaths.BaseDirectory}\n\n" +
                $"Déplacez PHONIE dans un dossier utilisateur inscriptible, puis relancez l'application.\n\n" +
                $"Détail : {exception.GetBaseException().Message}",
                "PHONIE - stockage portable indisponible",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            this.Shutdown(2);
            return;
        }

        var window = new MainWindow();
        this.MainWindow = window;
        window.Show();
    }
}
