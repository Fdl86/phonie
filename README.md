# PHONIE - DEV0.2.4 UI PREMIUM & PORTABLE

PHONIE est une application Windows x64 autonome destinée au contrôle aérien vocal VFR dans Microsoft Flight Simulator 2020 et 2024.

## Contenu de DEV0.2.4

Cette version consolide la base DEV0.2.3 validée en test réel.

- nouvelle interface plus dense et plus premium ;
- aucune barre de défilement verticale dans la fenêtre normale ;
- icône officielle PHONIE intégrée à la fenêtre et à l'exécutable ;
- gain micro PHONIE réglable de 0 à +18 dB ;
- limiteur léger à -1 dBFS ;
- affichage du niveau estimé après gain et alerte de saturation ;
- rejet automatique des appuis PTT inférieurs à 250 ms ;
- conservation d'un seul dernier PTT valide ;
- réglages, logs, enregistrements et cache stockés uniquement dans le dossier PHONIE ;
- rotation automatique des logs avec 10 fichiers maximum ;
- marqueurs de test nommés ;
- démarrage SimConnect plus propre avec lecture de chauffe et limitation des requêtes optionnelles en échec ;
- libération renforcée des périphériques audio ;
- uniquement des tirets normaux dans l'interface.

## Structure portable

Après le premier lancement :

```text
PHONIE\
├── PHONIE.exe
├── config\
│   └── settings.json
├── logs\
├── recordings\
│   └── last-ptt.wav
└── cache\
```

PHONIE ne crée volontairement aucun fichier dans AppData et n'utilise pas le registre Windows.

Le dossier contenant l'application doit être inscriptible. Si ce n'est pas le cas, PHONIE affiche une erreur claire et ne redirige pas silencieusement ses fichiers ailleurs.

## Build GitHub Actions

1. Copier le contenu complet de ce zip dans le dépôt PHONIE.
2. Pousser sur la branche `main` avec GitHub Desktop.
3. Ouvrir l'onglet Actions du dépôt.
4. Attendre le run vert `Build PHONIE DEV0.2.4`.
5. Télécharger l'artifact `PHONIE-DEV0.2.4-win-x64`.
6. Extraire le zip dans un dossier utilisateur inscriptible.
7. Lancer `PHONIE.exe`.

## Remarque de validation

Le code source a été contrôlé statiquement dans l'environnement de génération de cette livraison. Aucun SDK .NET local n'était disponible dans cet environnement. Le premier run GitHub Actions constitue donc la validation réelle de compilation Windows.

Voir `TEST-DEV0.2.4.md` pour le protocole conseillé.
