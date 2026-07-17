# Validation effectuée avant livraison

- structure XML vérifiée pour `App.xaml`, `MainWindow.xaml`, les deux thèmes et le projet ;
- noms des gestionnaires XAML comparés aux méthodes du code-behind ;
- équilibre des accolades et parenthèses contrôlé sur les fichiers C# ;
- workflow GitHub Actions relu et versionné DEV0.2.2 ;
- variables radio et météo enrichies protégées par des valeurs de repli afin de ne pas casser la connexion SimConnect principale.

La compilation WPF Windows reste à confirmer par le premier run GitHub Actions, l'environnement de préparation ne disposant pas du SDK .NET/WPF local.
