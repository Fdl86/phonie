# PHONIE DEV0.2.1 — Hotfix compilation

Correction ciblée du build Windows :

- ajout explicite de `using System.IO;` dans `SettingsService.cs` ;
- ajout explicite de `using System.IO;` dans `AudioService.cs` ;
- ajout de `src/Phonie/GlobalUsings.cs` dans le périmètre compilé du projet ;
- aucune modification fonctionnelle du moteur SimConnect, de l’audio, du PTT ou de l’interface.
