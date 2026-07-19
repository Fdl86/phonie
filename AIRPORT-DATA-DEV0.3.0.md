# Airport Data DEV0.3.0

PHONIE interroge la Facilities API de SimConnect pour l'OACI LFBI.

## Données conservées

- aérodrome ;
- pistes ;
- départs ;
- fréquences ;
- parkings ;
- points taxi ;
- chemins taxi ;
- noms de taxiways.

## Correctif TaxiPath

DEV0.2.5 omettait plusieurs champs situés entre `RUNWAY_DESIGNATOR` et `START`. Cela décalait le décodage d'une partie des chemins taxi.

DEV0.3.0 lit maintenant :

- demi-largeur gauche et droite ;
- type de bord gauche et droit ;
- éclairage des bords ;
- ligne centrale et éclairage ;
- index de début et de fin ;
- index du nom.

Les rapports ajoutent des avertissements en cas de compte, index, numéro ou désignateur incohérent.

## Scenery custom LFBI

La scène personnalisée peut se superposer à la base du simulateur. PHONIE conserve donc les doublons dans le JSON brut, puis les interprète dans l'interface sans supprimer arbitrairement une fréquence.
