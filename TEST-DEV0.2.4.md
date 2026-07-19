# Tests - PHONIE DEV0.2.4

## 1. Build

- vérifier que GitHub Actions termine en vert ;
- vérifier que l'artifact s'appelle `PHONIE-DEV0.2.4-win-x64` ;
- vérifier que `PHONIE.exe` affiche la nouvelle icône.

## 2. Interface

- lancer PHONIE sur un écran 1920 x 1080 ;
- vérifier qu'aucune barre de défilement verticale n'apparaît ;
- vérifier que le journal, le diagnostic de légèreté et la barre d'état restent entièrement visibles ;
- réduire la fenêtre jusqu'à sa taille minimale et vérifier que tout reste accessible ;
- tester le thème sombre puis le thème clair.

## 3. Stockage portable

Après le premier lancement, vérifier la présence de :

```text
config\
logs\
recordings\
cache\
```

Vérifier que :

- `config\settings.json` est créé ;
- le log courant est dans `logs\` ;
- aucun nouveau fichier PHONIE n'est créé dans `%LOCALAPPDATA%` ;
- le bouton `Ouvrir les logs` ouvre bien le sous-dossier local.

## 4. Gain micro

- tester 0 dB, +6 dB, +12 dB et +18 dB ;
- vérifier que le niveau visuel augmente avec le gain ;
- vérifier que l'avertissement de saturation apparaît lorsque le niveau est trop élevé ;
- enregistrer puis réécouter un PTT à +6 dB ;
- vérifier que le volume est nettement supérieur à celui de DEV0.2.3 sans distorsion excessive.

## 5. PTT

- tester le clavier ;
- tester le HOTAS ;
- vérifier qu'un appui très bref inférieur à 250 ms est ignoré ;
- vérifier qu'un PTT normal est conservé ;
- vérifier que seul `recordings\last-ptt.wav` reste présent ;
- redémarrer PHONIE et vérifier que le dernier PTT peut encore être lu.

## 6. SimConnect

- lancer PHONIE avant MSFS ;
- lancer MSFS 2020 ou 2024 ;
- vérifier que la connexion se stabilise sans rafale de messages de timeout optionnels ;
- changer COM1 entre TOUR, ATIS et APPROCHE ;
- vérifier une cadence proche de 1 Hz ;
- forcer une reconnexion manuelle.

## 7. Logs

- sélectionner plusieurs marqueurs nommés ;
- vérifier leur présence dans le log ;
- fermer PHONIE normalement ;
- vérifier la présence du résumé final ;
- relancer plusieurs fois et vérifier que le dossier reste limité à 10 logs maximum.
