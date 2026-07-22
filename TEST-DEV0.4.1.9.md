# Validation PHONIE DEV0.4.1.9

## CI obligatoire

Le workflow doit réussir intégralement :

1. contrôle des attributs Git et SHA-256 ;
2. restore des quatre projets ;
3. compilation PHONIE avec zéro avertissement ;
4. compilation Smoke Tests et Core Tests avec zéro avertissement ;
5. exécution des Smoke Tests ;
6. exécution des Core Tests ;
7. prépublication autonome Windows x64 ;
8. génération/validation SIA ;
9. publication finale et présence de PHONIE.exe.

## Essai MSFS prioritaire

- LFBI 118.505 doit afficher POITIERS TOUR et autoriser le dialogue.
- Premier appel complet avec « bonjour » : une seule salutation contrôleur.
- Deuxième « bonjour » sur la même Tour : aucune nouvelle salutation.
- Sol puis Tour : nouveau premier contact sur la Tour.
- Tour, ATIS, retour Tour avec « de retour » : historique et indicatif abrégé conservés.
- Premier message avec demande de roulage : instruction produite immédiatement.
- Appel explicite de Nantes Tour alors que Poitiers Tour est actif : silence.
- SIV/FIS régional avec demande de roulage : refus informatif, aucune clairance.
- Changement LFBI vers LFOU pendant le même vol : contexte rechargé, session de vol conservée.
- Restart Flight avec le même avion et le même indicatif : tous les contacts repartent de zéro.
- Retour menu puis nouveau vol : tous les contacts repartent de zéro.
- Bouton Diagnostic > Nouvelle session : remise à zéro immédiate sans redémarrage de PHONIE.


## Régressions CI DEV0.4.1.9

Le workflow doit confirmer :

- `prêt au point d'attente` produit une phrase contenant `piste deux un, alignez-vous` puis `autorisé décollage` ;
- `Poitiers Tour de retour avec vous` est classé `InitialContact` ;
- après écoute ATIS, le retour Tour répond `rebonjour` et conserve l'historique de l'organisme ;
- la suite Core termine avec `PHONIE Core tests OK - 65/65`.
- un premier appel contenant « bonsoir » reçoit une réponse contenant « bonsoir ».
