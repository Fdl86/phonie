# PHONIE DEV0.3.0.3 - CALLSIGN CONTEXT HOTFIX

PHONIE est une application Windows x64 portable destinée au contrôle aérien VFR vocal en français dans Microsoft Flight Simulator 2020 et 2024.

Cette version corrige le défaut observé pendant les essais DEV0.3.0.2 : des mots ordinaires comme `Tour`, `Voici` ou `Faut` pouvaient être transformés en faux indicatifs `T-OUR`, `V-OICI` ou `F-AUT`.

## Corrections principales

- l'identifiant `ATC ID` lu dans MSFS reste la référence contextuelle ;
- le nom de la station est retiré avant la recherche de l'indicatif ;
- une immatriculation écrite directement doit désormais contenir un tiret pour être acceptée sans contexte ;
- `Poitiers Tour` ne peut plus produire `T-OUR` ;
- `Voici` ne peut plus produire `V-OICI` ;
- `Faut` ne peut plus produire `F-AUT` ;
- reconnaissance de variantes françaises ou fautives : `novembre`, `yankees`, `yankis`, `Gold`, `Colf`, `Marvo` ;
- rapprochement séquentiel avec l'ATC ID, en tolérant quelques mots parasites ;
- rejet d'un indicatif appartenant clairement à un autre avion ;
- meilleure tolérance aux transcriptions `tours de pisse`, `tourne-piste` et `information Alpes` ;
- libération des modèles Whisper et Vosk après une comparaison afin d'éviter de les conserver simultanément en mémoire.

## Profils ASR conservés

- Whisper Base CPU ;
- Whisper Small CPU ;
- Whisper Small Vulkan ;
- Vosk FR expérimental.

Le profil Vulkan reste le meilleur résultat mesuré sur la RX 7800 XT, avec des transcriptions de l'ordre de quelques dixièmes de seconde sur les essais fournis.

## Contrôle automatique GitHub Actions

Le workflow compile l'application puis exécute un programme de tests sans dépendance externe. Les cas suivants doivent tous réussir avant la publication de l'archive :

- F-HNNY exact ;
- variante de roulage ;
- transcription Whisper réelle fournie pendant les essais ;
- transcription Vosk réelle fournie pendant les essais ;
- ancien indicatif F-GABC ;
- immatriculation écrite avec tiret ;
- absence de faux indicatif à partir de `Poitiers Tour` ;
- rejet de F-GABC lorsque l'avion du simulateur est F-HNNY.

## Limites connues

Le décodage détaillé de certains champs TaxiPath de la scène LFBI reste incorrect à partir de l'index 17. PHONIE conserve les points, parkings et connexions utiles, mais ne doit pas utiliser les numéros de piste absurdes comme données opérationnelles.

Commencer par `TEST-DEV0.3.0.3.md`.
