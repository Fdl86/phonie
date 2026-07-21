using System.Text;
using System.Windows;
using System.Windows.Threading;
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

        this.DispatcherUnhandledException += this.App_OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += AppDomain_OnUnhandledException;

        try
        {
            var window = new MainWindow();
            this.MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            var fatalPath = WriteStartupFatal("STARTUP", exception);
            MessageBox.Show(
                $"PHONIE n'a pas pu démarrer.\n\n" +
                $"Détail : {exception.GetBaseException().Message}\n\n" +
                $"Diagnostic : {fatalPath}",
                "PHONIE - erreur de démarrage",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            this.Shutdown(3);
        }
    }

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var fatalPath = WriteStartupFatal("DISPATCHER", e.Exception);
        MessageBox.Show(
            $"PHONIE a rencontré une erreur inattendue.\n\n" +
            $"Détail : {e.Exception.GetBaseException().Message}\n\n" +
            $"Diagnostic : {fatalPath}",
            "PHONIE - erreur inattendue",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        this.Shutdown(4);
    }

    private static void AppDomain_OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Erreur native inconnue");
        _ = WriteStartupFatal("APPDOMAIN", exception);
    }

    private static string WriteStartupFatal(string stage, Exception exception)
    {
        var path = Path.Combine(AppPaths.LogsDirectory, "startup-fatal.log");
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            var builder = new StringBuilder();
            builder.AppendLine($"PHONIE DEV0.4.1.1 - {stage}");
            builder.AppendLine($"Date : {DateTimeOffset.Now:O}");
            builder.AppendLine($"Dossier : {AppPaths.BaseDirectory}");
            builder.AppendLine($"Type : {exception.GetType().FullName}");
            builder.AppendLine($"Message : {exception.GetBaseException().Message}");
            builder.AppendLine();
            builder.AppendLine(exception.ToString());
            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
            return path;
        }
        catch
        {
            return "logs\\startup-fatal.log (écriture impossible)";
        }
    }
}
