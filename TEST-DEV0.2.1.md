# PHONIE DEV0.2.1 — protocole de test

## 1. Mise à jour propre

- fermer l'ancienne DEV0.1 ;
- remplacer le contenu du dépôt en conservant seulement `.git` ;
- pousser sur `main` ;
- télécharger uniquement l'artifact du dernier run vert ;
- vérifier que la fenêtre et l'exécutable portent bien le nom `PHONIE`.

## 2. Thèmes

- lancer PHONIE : le thème sombre doit être actif par défaut ;
- passer sur `Clair`, fermer et relancer ;
- vérifier que le choix est conservé ;
- revenir sur `Sombre`.

## 3. Audio automatique

- vérifier que le microphone et la sortie de communication Windows sont sélectionnés automatiquement ;
- parler sans PTT : le vumètre doit réagir ;
- changer de périphérique dans chaque liste ;
- fermer et relancer PHONIE : les choix doivent être conservés ;
- débrancher puis rebrancher un périphérique et cliquer sur `Actualiser`.

## 4. PTT

- laisser `Ctrl droit` ou choisir une autre touche ;
- placer MSFS au premier plan ;
- maintenir le PTT pendant 3 à 5 secondes et parler ;
- vérifier l'état `ÉMISSION — parlez` ;
- relâcher : une ligne `PTT enregistré` doit apparaître ;
- cliquer sur `Écouter le dernier PTT` ;
- vérifier la lecture sur la sortie choisie ;
- fermer et relancer : la touche PTT doit être conservée.

## 5. SimConnect 2020 et 2024

- tester PHONIE lancé avant MSFS puis après MSFS ;
- vérifier la bonne détection 2020/2024 ;
- vérifier position, distance LFBI, altitude, cap, IAS/GS, COM1 et transpondeur ;
- fermer puis relancer MSFS sans fermer PHONIE ;
- vérifier la reconnexion automatique.

## 6. OpenTrack et webcam

- lancer OpenTrack avec la webcam active ;
- lancer PHONIE avant puis après OpenTrack ;
- effectuer plusieurs appuis PTT ;
- confirmer l'absence de plantage ou de conflit.
