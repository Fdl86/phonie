# Changelog PHONIE DEV0.4.0.1

## Ajouté

- décodeur pur et little-endian de l'en-tête Facilities de 40 octets ;
- lecture explicite des métadonnées `UserRequestId`, `UniqueRequestId`, `ParentUniqueRequestId`, `FacilityType`, `IsListItem`, `ItemIndex` et `ListSize` ;
- décodeur TaxiPath de 64 octets avec trace des 16 champs ;
- capture des paquets bruts SimConnect dans `logs\airport-data\raw` ;
- rapports JSON et CSV destinés à identifier le premier paquet incohérent ;
- détection des tailles incohérentes, index dupliqués, manquants et hors plage ;
- état visible `DIAG TAXIPATH COHÉRENT` ou `DIAG TAXIPATH À TRANSMETTRE` ;
- tests synthétiques du décodeur Facilities et TaxiPath ;
- solution `PHONIE.sln` ;
- workflow Windows x64 mis à jour pour DEV0.4.0.1.

## Conservé

- tous les moteurs ASR et leurs profils ;
- PTT, audio, SimConnect, radio, ATIS texte et télémétrie ;
- préchauffage Turbo et benchmark GPU ;
- stockage portable.

## Non inclus

- graphe de taxiways ;
- calcul de route ;
- sélection d'un point d'attente ;
- occupation trafic ;
- machine d'état Ground Operations ;
- abréviation conversationnelle active de l'indicatif ;
- nouvelle synthèse vocale ATC.
