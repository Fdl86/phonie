# Tests — PHONIE DEV0.2.2

## 1. Interface sombre

- lancer PHONIE sans MSFS ;
- vérifier que la barre de titre, le fond, les cartes, les listes déroulantes et le journal sont réellement sombres ;
- ouvrir chaque ComboBox : aucun menu blanc ne doit apparaître ;
- passer en thème clair, fermer, relancer, puis revenir au thème sombre ;
- vérifier que le choix est mémorisé.

## 2. Audio et PTT

- vérifier le microphone et la sortie sélectionnés ;
- parler : le vumètre doit réagir et changer de couleur uniquement à fort niveau ;
- maintenir puis relâcher `Ctrl droit` ;
- réécouter le dernier PTT ;
- refaire le test avec PHONIE en arrière-plan.

## 3. MSFS 2020 puis MSFS 2024

Pour chaque simulateur :

- lancer PHONIE avant puis après MSFS ;
- vérifier la connexion et la bonne détection de version ;
- contrôler position, altitude, cap, vitesse, COM1 et transpondeur ;
- vérifier que les quatre indicateurs du pied de page passent dans l'état attendu.

## 4. Scanner radio LFBI

À LFBI :

- régler Poitiers Tour sur la fréquence publiée dans la base du simulateur, normalement `118.505` sur une base à jour ;
- vérifier l'affichage de l'identifiant `LFBI` et du type `TOUR/TWR` ;
- vérifier l'espacement `8,33 kHz` si la radio de l'avion l'expose ;
- modifier COM1 et confirmer que la carte **SERVICE RADIO** et le journal changent immédiatement.

Le scanner doit refléter la base réellement chargée dans MSFS, y compris une scène additionnelle qui modifie la fréquence.

## 5. Types de service

Selon les terrains disponibles dans le simulateur, essayer :

- une fréquence `ATIS` ou `AWS` : **Information automatique** ;
- une fréquence `FSS` : **Service d'information / AFIS** ;
- une fréquence `CTAF` ou `UNI` : **Auto-information**, PHONIE silencieux ;
- une fréquence inconnue : **Fréquence non identifiée**.

Aucun son ATIS n'est encore attendu dans cette DEV.

## 6. Météo locale

À proximité de LFBI :

- comparer grossièrement vent, QNH et température avec l'écran météo ou les instruments ;
- changer de preset météo et vérifier l'évolution des valeurs ;
- confirmer que l'application ne présente pas ces données comme la météo d'un terrain distant : elles sont mesurées à la position de l'avion.

## 7. OpenTrack et webcam

- lancer OpenTrack et la webcam avant PHONIE ;
- répéter avec PHONIE déjà ouvert ;
- effectuer plusieurs PTT et changements de COM1 ;
- aucun plantage ni perte durable du micro n'est attendu.

## Retour de test conseillé

```text
UI sombre complète : OK / KO
Menus déroulants sombres : OK / KO
Thème mémorisé : OK / KO
Audio/PTT : OK / KO
MSFS 2020 : OK / non testé
MSFS 2024 : OK / non testé
LFBI 118.505 identifié : ...
Type radio affiché : ...
Espacement affiché : ...
Météo locale : OK / KO
OpenTrack/webcam : OK / non testé
Erreur ou capture : ...
```
