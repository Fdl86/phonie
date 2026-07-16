# Procédure de test — ATC VFR DEV0.1

## Test principal LFBI

1. Lancer MSFS 2020 ou MSFS 2024.
2. Charger un vol à LFBI, au parking ou sur une piste.
3. Lancer `ATC-VFR.exe`.
4. Vérifier que le statut passe de **En attente du simulateur** à **Connecté**.
5. Vérifier que le simulateur détecté est correct.
6. Vérifier que la position, l'altitude, le cap, les vitesses et la distance LFBI évoluent.
7. Modifier la fréquence COM1 et le transpondeur dans l'avion puis vérifier l'affichage.

## Ordre de lancement

Tester les deux cas :

- application lancée avant MSFS ;
- application lancée après le chargement du vol.

Dans les deux cas, aucune action manuelle ne doit être nécessaire.

## Reconnexion

1. Laisser ATC VFR ouvert.
2. Fermer MSFS.
3. Vérifier que l'application revient en attente sans planter.
4. Relancer MSFS et charger un vol.
5. Vérifier la reconnexion automatique.

## OpenTrack / webcam

1. Lancer OpenTrack avec la webcam active.
2. Lancer MSFS et ATC VFR dans n'importe quel ordre.
3. Utiliser le suivi de tête pendant quelques minutes.
4. Vérifier qu'ATC VFR reste connecté et continue d'actualiser les données.

ATC VFR DEV0.1 n'accède jamais à la webcam et n'utilise aucune API vidéo.

## Informations à me transmettre en cas de problème

- MSFS 2020 ou MSFS 2024 ;
- texte exact du statut affiché ;
- cinq dernières lignes du journal ;
- moment où le problème survient ;
- présence ou non d'OpenTrack.
