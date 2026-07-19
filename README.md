# PHONIE DEV0.3.0.2 - ASR PROFILES & CALLSIGN FUZZY

PHONIE est une application Windows x64 portable destinée à construire un contrôle aérien VFR vocal en français pour Microsoft Flight Simulator 2020 et 2024.

DEV0.3.0.2 prolonge le jalon First Contact avec une vraie couche de comparaison des moteurs de reconnaissance et une récupération plus robuste de l'indicatif de l'avion.

## Nouveautés principales

- quatre profils de reconnaissance sélectionnables ;
- Whisper Base q5_1 CPU pour privilégier la vitesse ;
- Whisper Small q5_1 CPU pour privilégier la précision ;
- Whisper Small q5_1 avec runtime Vulkan pour tester l'accélération GPU ;
- Vosk Small FR 0.22 comme moteur expérimental indépendant ;
- comparaison de tous les profils installés sur le même fichier PTT ;
- lecture de l'identifiant ATC de l'avion directement depuis SimConnect ;
- rapprochement phonétique de la transcription avec cet identifiant ;
- tolérance aux variantes telles que Golf, Golfe, Gold, Alfa ou Charlie ;
- gain micro par défaut ramené à +9 dB pour les nouvelles installations ;
- conservation de tous les réglages, modèles, journaux et caches dans le dossier PHONIE.

## Profils ASR

### Whisper Base CPU - rapide

Modèle `ggml-base-q5_1.bin`, environ 57 Mio. Il vise une latence plus faible que le modèle Small.

### Whisper Small CPU - équilibré

Modèle `ggml-small-q5_1.bin`, environ 181 Mio. Il reste le profil CPU de précision.

### Whisper Small Vulkan - GPU

Utilise le même modèle Small avec le runtime Vulkan. Le runtime Whisper est choisi au démarrage. Le passage CPU vers Vulkan, ou Vulkan vers CPU, demande donc un redémarrage de PHONIE.

Le runtime Vulkan peut revenir au runtime CPU si Vulkan ne peut pas être chargé. PHONIE ne bascule jamais silencieusement vers Vosk.

### Vosk FR - expérimental

Utilise `vosk-model-small-fr-0.22`. Ce profil est séparé de Whisper et sert à mesurer la vitesse et la qualité sur la phraséologie ATC française.

## Installation des modèles

Les modèles ne sont pas inclus dans l'archive compilée. Sélectionner un profil puis cliquer sur `Installer profil`.

Les téléchargements sont vérifiés par SHA-256 avant installation :

```text
PHONIE\models\whisper\ggml-base-q5_1.bin
PHONIE\models\whisper\ggml-small-q5_1.bin
PHONIE\models\vosk\vosk-model-small-fr-0.22\
```

## Indicatif issu du simulateur

PHONIE lit la SimVar `ATC ID`. Cet identifiant devient la référence contextuelle pour analyser la transmission.

Exemple :

```text
ATC ID SimConnect : F-GABC
Transcription : Fox Colf Alfa Bravo Charlie
Résultat PHONIE : F-GABC - rapprochement phonétique
```

PHONIE ne remplace pas arbitrairement une suite de mots par un indicatif. La détection utilise l'ordre des lettres, la proximité phonétique et un seuil de confiance.

## Comparaison sur le même PTT

Le bouton `Comparer` reprend le dernier enregistrement et exécute tous les profils compatibles déjà installés. Aucun nouveau son n'est enregistré entre les essais.

Un démarrage CPU compare :

```text
Whisper Base CPU
Whisper Small CPU
Vosk FR
```

Un démarrage Vulkan compare :

```text
Whisper Small Vulkan
Vosk FR
```

## Périmètre First Contact conservé

- connexion MSFS 2020 et MSFS 2024 ;
- PTT clavier et HOTAS ;
- Airport Data LFBI ;
- ATIS texte ;
- classification opérationnelle des fréquences ;
- silence sur ATIS, répondeur et fréquence inconnue ;
- première réponse écrite de la TOUR ;
- export des échanges dans `logs\sessions`.

## Stockage portable

```text
PHONIE\
|-- PHONIE.exe
|-- config\
|-- logs\
|   |-- airport-data\
|   `-- sessions\
|-- models\
|   |-- whisper\
|   `-- vosk\
|-- recordings\
`-- cache\
```

PHONIE n'utilise ni AppData ni le registre pour ses propres données. Le dossier d'installation doit être accessible en écriture.

## Limite Airport Data connue

Les TaxiPaths du scenery LFBI custom produisent encore des champs de piste incohérents dans la structure brute. PHONIE les signale comme avertissements et ne doit pas les utiliser comme données opérationnelles valides. Cette version reste centrée sur la reconnaissance vocale et l'indicatif.

## Compilation

Le workflow GitHub Actions produit une version Windows x64 autonome. Le SDK .NET Windows n'étant pas disponible dans l'environnement de préparation, GitHub Actions reste la validation réelle de compilation.

Commencer par `TEST-DEV0.3.0.2.md`.
