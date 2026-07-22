# PHONIE DEV0.4.1.5 - FULL BUILD CLOSURE HOTFIX

DEV0.4.1.5 ferme le dernier diagnostic de compilation observé dans DEV0.4.1.4 : `HttpClient` n'était pas résolu dans `RadioDataUpdateService.cs`. Le fichier importe maintenant explicitement `System.Net.Http`.

La CI est renforcée pour éviter les boucles coûteuses :

- compilation du projet WPF principal en premier avec zéro avertissement ;
- compilation et exécution des deux suites de tests C# ;
- prépublication autonome Windows x64 avant toute génération réseau ;
- génération et validation SIA seulement après validation complète du code ;
- publication finale et vérification de l'artefact portable.

## État fonctionnel conservé

- détection dynamique d'aérodrome et rechargement après changement ou téléportation ;
- contexte géographique distinct du contexte radio ;
- géométrie Facilities, pistes, parkings, taxiways et points d'attente ;
- routage sol, trafic et séquence parking vers point d'attente ;
- indicatif SimConnect et formes abrégées ;
- PTT, Whisper, Vosk et protections contre les indicatifs inventés ;
- base radio nationale SIA sans fréquence opérationnelle codée en dur ;
- silence sur A/A, CTAF, UNICOM, ATIS et messages enregistrés ;
- priorité Tour, Sol/Clairance, puis Approche/Départ ;
- voix contrôleur stable par station pendant la session ;
- stockage strictement portable dans le dossier PHONIE.

Commencer par `TEST-DEV0.4.1.5.md`.
