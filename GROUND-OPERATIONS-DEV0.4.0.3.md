# Moteur Ground Operations - DEV0.4.0.3

## Chaîne de décision

1. classification du service radio ;
2. validation de l'ATC ID ;
3. validation du modèle aérodrome ;
4. localisation réelle de l'avion ;
5. reconnaissance de l'intention ;
6. contrôle de compatibilité avec l'état de session ;
7. sélection de la piste en service ;
8. validation de la connaissance du trafic ;
9. recherche du point d'attente nommé et libre ;
10. calcul du chemin ;
11. création d'une décision structurée ;
12. génération et synthèse de l'énoncé complet.

## Graphe

Chaque parking ou point taxi devient un nœud. Les TaxiPaths valides deviennent des arêtes pondérées par leur longueur. Les chemins sans nom reçoivent une pénalité de routage mais restent utilisables comme connecteurs silencieux.

Les routes fermées, véhicules, routières, peintes et les segments de piste sont exclus du trajet normal vers le point d'attente.

## Occupation

Lorsque l'avion est au sol et à proximité de l'aérodrome actif, PHONIE demande à SimConnect les objets avions proches toutes les deux secondes. Le traitement est déclenché par événement et ne lance aucune boucle intensive indépendante.

Un trafic récent au sol peut occuper :

- un nœud dans un rayon de 45 m ;
- un segment dans un corridor de 35 m.

L'avion utilisateur est retiré par son ATC ID et par comparaison de position. Si le fournisseur trafic est indisponible, l'occupation vaut `Unknown` et aucune clairance de roulage n'est émise.

## Machine d'état

`Unknown -> Parked -> StartupRequested -> ReadyToTaxi -> TaxiClearanceIssued -> Taxiing -> AtHoldShort -> ReadyForDeparture -> LineUpCleared -> LinedUp -> TakeoffCleared -> Airborne`

Les observations du simulateur confirment les transitions importantes. Une simple phrase ne peut pas autoriser un décollage depuis le parking.

## Journaux

Chaque décision est écrite en JSONL dans `logs\ground-operations` avec :

- horodatage ;
- action ;
- code de raison ;
- texte parlé et message système ;
- état avant/après ;
- indicatif complet et abrégé ;
- piste, point d'attente, taxiways et distance ;
- confiance.


## Correctif DEV0.4.0.3 - occupation et départ LFBI

- l'objet avion utilisateur SimConnect est exclu explicitement de l'occupation ;
- un second filtre de sécurité utilise l'indicatif, la proximité et la vitesse ;
- les rayons d'occupation sont resserrés pour éviter qu'un seul appareil ferme plusieurs voies parallèles sur l'aire de trafic ;
- les décisions JSONL enregistrent désormais la position résolue, les nœuds occupés et les segments occupés ;
- les segments de piste restent exclus du roulage normal jusqu'au point d'attente ;
- la remontée de piste et le départ depuis une intersection seront traités comme une phase distincte après le point d'attente, jamais comme un contournement du réseau taxi.
