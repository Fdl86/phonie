# Contextes aérodrome et radio - DEV0.4.0.9

## Détection géographique

PHONIE demande la liste des aérodromes du cache Facilities proche avec `SimConnect_RequestFacilitiesList_EX1`.

Le paquet natif contient un en-tête de 28 octets et des entrées MSFS 2024 de 36 octets : `ident[9]`, `region[3]`, latitude, longitude et altitude. Avec SimConnect.NET 0.2.1, les logs réels montrent un emplacement de compatibilité supplémentaire :

- 161 éléments déclarés, 162 emplacements reçus ;
- 204 éléments déclarés, 205 emplacements reçus.

Le décodeur accepte uniquement `dwArraySize` ou `dwArraySize + 1` emplacements de taille connue. L'emplacement supplémentaire de compatibilité est toujours ignoré, même s'il contient des octets résiduels qui ressemblent à une entrée valide. Toute autre disposition reste refusée et journalisée.

## Changement d'aérodrome

Un spawn, une téléportation ou un déplacement important déclenche la recherche du terrain courant. Lors d'un changement d'ICAO, PHONIE invalide l'ancien contexte et recharge Facilities, pistes, fréquences, météo, graphe sol et diagnostic.

## Contexte radio

Le contexte radio reste indépendant du contexte géographique. En vol, la fréquence COM active peut cibler l'aérodrome d'arrivée avant que celui-ci ne devienne le contexte sol.

## Fréquences vérifiées

`data/radio/france-official.json` contient une amorce vérifiée limitée à LFBI et LFOU pour cette candidate. Les autres terrains restent détectés dynamiquement et utilisent les Facilities comme secours radio prudent tant que leur fiche officielle n'est pas encore intégrée. Pour les terrains présents dans ce catalogue, les données officielles prennent priorité sur les fréquences de scène.

Ordre de recommandation : Tour, Sol/Clairance, Approche/Départ, AFIS/FSS. A/A, CTAF, UNICOM, ATIS, AWS et messages enregistrés restent silencieux.
