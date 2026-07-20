# Protocole PHONIE DEV0.4.0.1 - FACILITIES DIAGNOSTIC

## 1. Compilation GitHub Actions

Après copie du ZIP source dans le dépôt et envoi avec GitHub Desktop, le workflow doit afficher :

```text
Build succeeded.
PHONIE smoke tests OK
```

L'artefact attendu est :

```text
PHONIE-DEV0.4.0.1-win-x64.zip
```

## 2. Test principal LFBI

1. Extraire l'artefact portable dans un dossier neuf.
2. Lancer MSFS 2024 à LFBI, avion placé sur un parking.
3. Lancer PHONIE et attendre la connexion SimConnect.
4. Ouvrir `Diagnostic`.
5. Cliquer sur `Capturer LFBI`.
6. Attendre le message de fin dans le journal.
7. Vérifier l'état affiché dans la carte aérodrome.

Résultat attendu :

- PHONIE ne se fige pas ;
- un rapport texte et un rapport JSON apparaissent dans `logs\airport-data` ;
- un nouveau dossier apparaît dans `logs\airport-data\raw` ;
- ce dossier contient `diagnostic-summary.json`, `packets.csv`, `taxipath-fields.csv`, `LISEZ-MOI.txt` et des fichiers `.bin` ;
- aucune nouvelle clairance de roulage n'est générée par ce build.

## 3. Fichiers à transmettre

Fermer PHONIE, puis compresser et transmettre le dossier complet le plus récent situé dans :

```text
PHONIE\logs\airport-data\raw\
```

Joindre également les deux fichiers `airport-*.txt` et `airport-*.json` correspondants, situés directement dans `logs\airport-data`.

Ne pas modifier les CSV, JSON ou fichiers binaires avant transmission.

## 4. Test MSFS 2020 recommandé

Répéter la capture sous MSFS 2020 avec la même scène LFBI lorsque possible. Les deux dossiers permettront de déterminer si la différence vient du simulateur, de la scène ou du décodage commun.

## 5. Non-régression rapide

Vérifier ensuite :

- connexion SimConnect ;
- scanner radio ;
- PTT clavier ;
- enregistrement d'un WAV ;
- transcription avec le profil ASR habituel ;
- absence de réponse sur ATIS et 124.000 ;
- présence des dossiers portables `config`, `logs`, `recordings`, `cache` et `models`.
