# CHANGELOG - PHONIE DEV0.4.0.5

## GROUND OPERATIONS FINAL RC

### Noms et procédures opérationnels

- catalogue portable `data\airports` ;
- premier profil validé LFBI ;
- association géographique des points officiels au graphe Facilities ;
- distinction point intermédiaire, point d'attente de départ et entrée de piste ;
- résolution LFBI A3 / A2 / A sans dépendance aux index de scène ;
- repli sûr sur une formulation générique lorsque l'appellation n'est pas fiable.

### Roulage et départ

- depuis S6, sélection du point d'attente A2 ;
- exclusion d'A3 comme destination finale ;
- phraséologie courte sans récitation de tous les segments ;
- reconnaissance de `prêt en Alpha 2` ;
- départ depuis l'intersection et remontée de piste ;
- extension de la machine d'état jusqu'au décollage ;
- vérification de la position réelle.

### AFIS

- même moteur géométrique ;
- réponses informatives uniquement ;
- aucune clairance impérative ni autorisation de décollage.

### Collationnement

- attente d'un PTT après les messages Tour et AFIS ;
- contenu non bloquant ;
- relances `collationnez` puis `accusez réception` ;
- le PTT de collationnement n'est pas traité comme une nouvelle intention.

### Diagnostic

- carte vectorielle du réseau ;
- affichage route, points, entrée de piste, avion, trafic et occupations ;
- source et confiance des appellations ;
- journaux JSONL enrichis.

### Audio et ASR

- redémarrage ASR dans la session courante ;
- libération explicite des modèles ;
- redémarrage de la synthèse contrôleur ;
- arrêt de la lecture en cours ;
- changement CPU/Vulkan toujours signalé comme nécessitant une relance complète.

### Livraison

- version visible DEV0.4.0.5 ;
- artefact Windows à extraction unique ;
- suppression du ZIP imbriqué ;
- tests LFBI, AFIS, collationnement et profil opérationnel.

## CI compilation hotfix

- Renommage de la variable locale de rappel de roulage pour supprimer CS0136.
- Garde nullable explicite sur le parking associé pour supprimer CS8602.
- Workflow restore/build rendu fail-fast après chaque commande dotnet.
