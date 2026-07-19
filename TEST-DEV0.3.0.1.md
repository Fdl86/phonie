# Protocole de test PHONIE DEV0.3.0.1

## 1. Compilation et démarrage

- Lancer le workflow GitHub Actions.
- Vérifier que l'archive `PHONIE-DEV0.3.0.1-win-x64.zip` est produite.
- Extraire l'archive dans un dossier accessible en écriture.
- Lancer `PHONIE.exe`.
- Vérifier l'affichage `DEV0.3.0.1 - FIRST CONTACT`.
- Agrandir la fenêtre et vérifier que la barre inférieure reste au-dessus de la barre des tâches.
- Vérifier l'absence de scroll vertical dans une zone de travail 1920 x 1080 standard.

## 2. Stockage portable

Vérifier la création locale de :

```text
config
logs
logs\airport-data
logs\sessions
models\whisper
recordings
cache
```

Aucun nouveau dossier PHONIE ne doit apparaître dans AppData.

## 3. Whisper Small q5_1

- Cliquer sur `Télécharger Small q5_1`.
- Vérifier la progression.
- Attendre le statut `prêt - 181 Mio`.
- Si le runtime natif ne charge pas, relever le message exact avant toute installation ; le prérequis attendu est le redistribuable Microsoft Visual C++ x64 2019 ou plus récent.
- Vérifier la présence de `models\whisper\ggml-small-q5_1.bin`.
- Fermer puis relancer PHONIE et vérifier que le modèle est détecté sans nouveau téléchargement.

## 4. Mode laboratoire sans simulateur

Saisir puis analyser :

```text
Poitiers Tour, Fox Golf Alpha Bravo Charlie, au parking aviation générale pour tours de piste avec information Alpha.
```

Vérifier l'extraction :

- station : Poitiers Tour ;
- indicatif : F-GABC ;
- position : parking aviation générale ;
- intention : tours de piste ;
- ATIS : A.

Sans fréquence contrôlée active, PHONIE doit rester silencieux ou indiquer que le service n'autorise pas encore le dialogue.

## 5. MSFS 2020 puis MSFS 2024 à LFBI

- Charger l'avion au parking de LFBI.
- Attendre la connexion SimConnect.
- Vérifier la lecture automatique Airport Data. Utiliser `Lire LFBI` dans Diagnostic si nécessaire.
- Vérifier la fiche aérodrome, les pistes, parkings, taxiways et fréquences.
- Vérifier la génération de l'ATIS texte et la piste proposée selon le vent.
- Vérifier que les rapports Airport Data ne contiennent plus les très grandes valeurs absurdes de `RunwayNumber` et `RunwayDesignator` observées en DEV0.2.5.

## 6. Classification radio LFBI

Tester les fréquences disponibles dans le simulateur :

- `118.505` - Tour, dialogue autorisé ;
- `118.500` - Tour ou doublon, dialogue autorisé ;
- `121.780` - ATIS, aucune réponse ;
- `124.000` - répondeur enregistré, aucune réponse dialoguée ;
- `134.100` - Approche / SIV, dialogue autorisé.

## 7. Premier contact vocal

Sur la fréquence Tour opérationnelle :

- régler le gain micro, commencer à `+12 dB` si le signal reste faible ;
- maintenir le PTT ;
- prononcer la phrase de test complète ;
- relâcher le PTT ;
- vérifier la transcription, l'analyse et la première réponse ATC écrite ;
- vérifier le temps de traitement affiché ;
- vérifier l'export dans `logs\sessions\radio-AAAAMMJJ.jsonl`.

Refaire le test avec :

```text
Poitiers Tour, F-GABC, au parking pour tours de piste avec information Alpha.
```

## 8. Non-réponse obligatoire

Répéter un PTT sur `121.780` puis `124.000`.

PHONIE peut transcrire et analyser le message, mais ne doit générer aucune réponse de contrôleur.

## 9. Diagnostic

Ouvrir `Diagnostic`, effectuer plusieurs PTT et laisser PHONIE tourner au moins dix minutes.

Envoyer en cas d'anomalie :

- le log principal ;
- le rapport JSON Airport Data ;
- le fichier `logs\sessions\radio-*.jsonl` ;
- une capture de l'interface ;
- le texte exact prononcé.
