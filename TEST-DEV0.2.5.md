# Test PHONIE DEV0.2.5

## 1. Vérification générale

- Lancer PHONIE depuis son dossier portable.
- Vérifier que la fenêtre affiche `DEV0.2.5`.
- Vérifier que l'UI tient toujours dans la fenêtre sans défilement vertical.
- Vérifier que les dossiers `config`, `logs`, `logs\airport-data`, `recordings` et `cache` restent dans le dossier PHONIE.

## 2. MSFS 2020 à LFBI

- Lancer MSFS 2020 et charger un avion à LFBI.
- Attendre la connexion SimConnect.
- Attendre environ 15 secondes, puis vérifier la ligne `Airport Data` dans la carte Simulateur.
- Si aucune lecture automatique ne démarre, cliquer sur `Lire LFBI`.
- Vérifier la création d'un JSON et d'un TXT dans `logs\airport-data`.
- Vérifier dans le TXT les pistes, fréquences, parkings et taxiways reçus.
- La fréquence tour doit être celle fournie par MSFS 2020, sans correction manuelle imposée par PHONIE.

## 3. Radio et PTT

- Changer COM1 entre TOUR, ATIS et APPROCHE.
- Vérifier que le scanner radio continue de fonctionner.
- Faire un PTT clavier et un PTT HOTAS.
- Vérifier qu'Airport Data ne perturbe ni l'audio ni la cadence SimConnect.

## 4. MSFS 2024

- Fermer proprement PHONIE et MSFS 2020.
- Refaire le test avec MSFS 2024 à LFBI.
- Vérifier que le rapport provient bien de MSFS 2024.
- Comparer les fréquences, pistes et installations avec le rapport MSFS 2020.

## 5. Fichiers à transmettre

Envoyer les quatre fichiers produits :

- JSON MSFS 2020 ;
- TXT MSFS 2020 ;
- JSON MSFS 2024 ;
- TXT MSFS 2024.

Ajouter le log principal de la session si une lecture échoue, reste bloquée ou renvoie un nombre incohérent d'éléments.
