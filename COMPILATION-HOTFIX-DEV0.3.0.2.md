# PHONIE DEV0.3.0.2 - Correctif compilation

Le premier workflow a révélé une erreur unique dans `SpeechRecognitionService.cs` : une propriété de type `List<RuntimeLibrary>` recevait un tableau `RuntimeLibrary[]`.

Le correctif utilise désormais des expressions de collection C# 12 directement ciblées vers le type attendu par Whisper.net 1.7.4.

Aucune fonctionnalité n'a été retirée et le numéro de version reste DEV0.3.0.2.
