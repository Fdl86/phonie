# Tests — PHONIE DEV0.2.3

## 1. Compilation et lancement

- vérifier que l'artifact porte le nom `PHONIE-DEV0.2.3-win-x64` ;
- lancer `PHONIE.exe` sans MSFS ;
- vérifier l'ouverture en thème sombre et l'absence d'erreur rouge ;
- relever le chemin affiché dans la carte **Diagnostic de légèreté**.

## 2. Affectation HOTAS

1. Cliquer sur **Assigner un bouton HOTAS**.
2. Appuyer une seule fois sur le bouton souhaité.
3. Vérifier l'affichage du nom du périphérique, du numéro de bouton et de l'état `prêt`.
4. Mettre PHONIE en arrière-plan.
5. Maintenir le bouton, parler trois à cinq secondes, puis relâcher.
6. Réécouter le dernier PTT.

Résultat attendu : l'enregistrement commence à l'appui et s'arrête à la relâche, même lorsque MSFS est au premier plan.

## 3. Persistance et reconnexion HOTAS

- fermer puis relancer PHONIE : l'affectation doit être conservée ;
- débrancher le HOTAS : l'interface doit afficher `déconnecté` sans plantage ;
- rebrancher le HOTAS : l'état doit revenir à `prêt` sous quelques secondes ;
- refaire un PTT sans réassigner le bouton.

## 4. Clavier et HOTAS ensemble

- vérifier que le PTT clavier fonctionne toujours ;
- maintenir le bouton HOTAS puis appuyer sur le PTT clavier ;
- relâcher l'un des deux : l'enregistrement doit continuer tant que l'autre reste appuyé ;
- relâcher le second : l'enregistrement doit s'arrêter.

## 5. MSFS 2020 / OpenTrack

- lancer MSFS 2020 et OpenTrack ;
- vérifier la connexion SimConnect, la météo et le scanner COM1 ;
- réaliser dix PTT HOTAS de durées différentes ;
- changer plusieurs fois de fréquence ;
- cliquer sur **Marquer le test** avant et après la série.

## 6. Endurance et légèreté

Laisser tourner PHONIE pendant au moins 20 minutes :

- cinq minutes au parking sans action ;
- cinq minutes avec changements de COM1 ;
- cinq minutes avec dix PTT ;
- cinq minutes en vol avec OpenTrack.

Objectifs indicatifs hors future transcription/synthèse :

```text
CPU moyen PHONIE : idéalement < 1 %
CPU au repos : idéalement < 0,5 %
Mémoire : cible < 150 Mo
Cadence SimConnect : proche de 1 Hz
```

Ces valeurs sont des objectifs de mesure, pas des seuils masquant un dysfonctionnement.

## 7. Fichier à envoyer

Fermer PHONIE proprement pour écrire le résumé de session, puis cliquer sur **Ouvrir les logs** au prochain lancement ou ouvrir :

```text
%LOCALAPPDATA%\PHONIE\Diagnostics
```

Envoyer le fichier `.log` correspondant au test.

Retour rapide conseillé :

```text
Compilation : OK / KO
HOTAS détecté : ...
Bouton assigné : ...
PTT HOTAS arrière-plan : OK / KO
Persistance : OK / KO
Débranchement/rebranchement : OK / KO
PTT clavier + HOTAS simultanés : OK / KO
MSFS 2020 : OK / KO
OpenTrack : OK / KO
CPU moyen du résumé : ...
CPU maximum : ...
Mémoire maximum : ...
Erreur observée : ...
```
