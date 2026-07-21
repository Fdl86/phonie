# Test DEV0.4.0.7 - protocole succinct

## 1. Build

GitHub Actions doit être entièrement vert : compilation, Smoke Tests, Core Tests et publication. Après téléchargement, une seule décompression doit donner directement `PHONIE.exe`.

## 2. Spawn sur un terrain autre que LFBI

Démarrer par exemple à LFOU.

Attendu sous quelques secondes :

- ICAO géographique `LFOU` ;
- chargement des pistes, parkings, taxiways et points d'attente de LFOU ;
- fréquences LFOU affichées ;
- aucune piste, fréquence ou règle LFBI conservée ;
- carte Diagnostic reconstruite pour LFOU.

## 3. Téléportation

Téléporter ensuite l'avion de LFOU vers LFBI, puis revenir à LFOU.

Attendu : changement automatique dans les deux sens, sans bouton manuel et sans redémarrer PHONIE.

## 4. Contexte radio en vol

En vol à distance du terrain d'arrivée, régler une fréquence Approche ou Tour d'un autre aérodrome.

Attendu :

- le contexte radio bascule vers la station accordée ;
- le contexte géographique reste lié à la position de l'avion ;
- les fréquences et Facilities de l'aérodrome radio sont chargées ;
- aucune station n'est inventée si COM1 reste ambigu.

## 5. Roulage et point d'attente

Sur une Tour : demander le roulage.

Attendu : `roulez au point d'attente et rappelez prêt`, sans nom local.

S'arrêter à n'importe quel véritable point d'attente Facilities, puis annoncer prêt au départ.

Attendu si trafic connu et piste libre : alignement et autorisation de décollage. Un profil local marqué « intermédiaire » ne doit plus bloquer la séquence.

## 6. Sécurités

- hors point d'attente : aucune autorisation de décollage ;
- piste occupée : maintien ;
- trafic indisponible : maintien ;
- AFIS : informations uniquement ;
- absence de collationnement PTT : relance.

En cas de défaut, conserver le ZIP du dossier `logs`, l'ICAO, la fréquence COM1, la position et la phrase prononcée.
