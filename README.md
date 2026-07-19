# PHONIE DEV0.2.5

PHONIE est une application Windows x64 portable destinée au contrôle vocal VFR dans Microsoft Flight Simulator 2020 et 2024.

## Contenu de cette version

DEV0.2.5 conserve la base validée de DEV0.2.4 :

- connexion et reconnexion SimConnect ;
- détection MSFS 2020 ou MSFS 2024 ;
- état de vol, position, cap, vitesses, COM1, transpondeur et météo locale ;
- PTT clavier global et PTT joystick/HOTAS ;
- enregistrement, réécoute et gain micro PHONIE ;
- interface sombre ou claire compacte ;
- diagnostic CPU, mémoire, handles et cadence SimConnect ;
- stockage portable dans le dossier PHONIE.

Cette version ajoute le module expérimental Airport Data :

- lecture d'un aérodrome par code OACI avec la Facilities API de SimConnect ;
- pistes, départs, fréquences, parkings et taxiways ;
- demande automatique lorsque l'OACI est identifiable ;
- bouton manuel `Lire LFBI` pour la première campagne de test ;
- rapports JSON et TXT dans `logs\airport-data` ;
- conservation des dix derniers rapports.

## Arborescence portable

```text
PHONIE\
|-- PHONIE.exe
|-- config\
|   `-- settings.json
|-- logs\
|   |-- PHONIE-DEV0.2.5-*.log
|   `-- airport-data\
|       |-- airport-*.json
|       `-- airport-*.txt
|-- recordings\
|   `-- last-ptt.wav
`-- cache\
```

PHONIE ne cherche pas à écrire ses réglages, logs, enregistrements ou caches dans AppData ou le registre. Le dossier d'installation doit donc être accessible en écriture.

## Compilation

Le workflow GitHub Actions produit un dossier Windows x64 autonome. Aucun SDK MSFS ni runtime .NET ne doit être installé sur le PC utilisateur.

Voir `TEST-DEV0.2.5.md` et `AIRPORT-DATA-DEV0.2.5.md` avant la campagne LFBI.
