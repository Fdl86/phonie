# PHONIE — DEV0.2 Audio & PTT

PHONIE est une application Windows de contrôle aérien vocal VFR destinée à Microsoft Flight Simulator 2020 et Microsoft Flight Simulator 2024.

## Contenu de DEV0.2

- renommage intégral du projet et de l'exécutable en `PHONIE` ;
- compatibilité SimConnect validée avec MSFS 2020 et MSFS 2024 ;
- reconnexion automatique sans ordre de lancement imposé ;
- sélection automatique du périphérique de communication Windows ;
- choix manuel du microphone et de la sortie radio dans l'interface ;
- mémorisation des périphériques choisis ;
- vumètre du microphone ;
- PTT clavier global, actif même lorsque MSFS possède le focus ;
- touche PTT configurable, `Ctrl droit` par défaut ;
- enregistrement local pendant l'appui PTT ;
- réécoute du dernier enregistrement sur la sortie choisie ;
- thème sombre ou clair, mémorisé ;
- journal SimConnect moins répétitif ;
- aucun accès à la webcam et aucune interaction avec OpenTrack.

DEV0.2 ne contient pas encore Whisper ni le moteur de phraséologie ATC.

## Construction

1. Conserver uniquement `.git` dans le dossier local du dépôt.
2. Copier le contenu complet de ce zip dans le dépôt.
3. Commit puis push sur `main` avec GitHub Desktop.
4. Attendre le run vert **Build PHONIE DEV0.2**.
5. Télécharger l'artifact `PHONIE-DEV0.2-win-x64`.
6. Décompresser puis lancer `PHONIE.exe`.

Aucun SDK MSFS, Visual Studio, .NET, Python ou Node.js n'est requis sur le PC de test.

## Données locales

Les préférences et les enregistrements de test sont stockés dans :

```text
%LOCALAPPDATA%\PHONIE
```

Voir `TEST-DEV0.2.md` pour le protocole conseillé.
