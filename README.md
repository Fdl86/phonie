# PHONIE — DEV0.2.2 Interface & scanner radio

PHONIE est une application Windows de contrôle aérien vocal VFR destinée à Microsoft Flight Simulator 2020 et Microsoft Flight Simulator 2024.

## Contenu de DEV0.2.2

- refonte complète de l'interface selon la direction visuelle CAP CLAIR ;
- véritable mode sombre, y compris barre de titre, listes, menus déroulants, journal et états désactivés ;
- thème clair cohérent, avec mémorisation du choix ;
- cartes plus compactes et barre d'état SimConnect / Audio / PTT ;
- états PTT plus lisibles et vumètre micro à trois niveaux ;
- conservation de l'audio, du PTT global et de la réécoute validés en DEV0.2.1 ;
- scanner de la station actuellement réglée sur COM1 : identifiant, type de service, espacement 25/8,33 kHz, réception et statut ;
- première règle de comportement radio : contrôle, information/AFIS, ATIS/AWS, auto-information ou fréquence inconnue ;
- lecture de la météo locale du simulateur : vent, QNH, température et visibilité ;
- données enrichies rendues optionnelles pour ne pas casser la connexion si un avion ou une version de MSFS n'expose pas une SimVar ;
- compatibilité MSFS 2020 et MSFS 2024 conservée ;
- aucune interaction avec la webcam ou OpenTrack.

DEV0.2.2 ne diffuse pas encore d'ATIS et ne contient pas encore Whisper. Elle valide d'abord l'environnement radio, les données météo et la nouvelle interface.

## Règles radio posées

- `TWR`, `GND`, `CLR`, `APPR`, `DEP` : organisme contrôlé, PHONIE répondra au pilote ;
- `FSS` : service d'information / AFIS, PHONIE pourra répondre et transmettre les paramètres ;
- `ATIS`, `AWS` : information automatique, diffusion sans dialogue ;
- `CTAF`, `UNI` : auto-information, PHONIE reste silencieux ;
- service inconnu : aucune réponse automatique.

Cette première classification dépend de ce que la base du simulateur expose. Les profils aérodrome officiels compléteront ensuite les cas où MSFS ne distingue pas correctement AFIS et auto-information.

## Construction

1. Conserver uniquement `.git` dans le dossier local du dépôt.
2. Copier le contenu complet de ce zip dans le dépôt.
3. Commit puis push sur `main` avec GitHub Desktop.
4. Attendre le run vert **Build PHONIE DEV0.2.2**.
5. Télécharger l'artifact `PHONIE-DEV0.2.2-win-x64`.
6. Décompresser puis lancer `PHONIE.exe`.

Aucun SDK MSFS, Visual Studio, .NET, Python ou Node.js n'est requis sur le PC de test.

## Prérequis dans MSFS

Désactiver les communications radio IA et les voix de l'ATC intégré afin d'éviter que PHONIE et l'ATC de Microsoft Flight Simulator parlent en même temps.

## Données locales

Les préférences et les enregistrements de test sont stockés dans :

```text
%LOCALAPPDATA%\PHONIE
```

Voir `TEST-DEV0.2.2.md` pour le protocole conseillé.
