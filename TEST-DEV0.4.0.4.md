# Protocole rapide PHONIE DEV0.4.0.4

## 1. GitHub Actions

Le workflow doit être entièrement vert :

- précontrôle des fichiers ;
- restauration des quatre projets ;
- compilation Release ;
- SmokeTests DEV0.3 ;
- tests Ground Operations ;
- publication Windows x64 ;
- création de `PHONIE-DEV0.4.0.4-win-x64.zip`.

## 2. Test principal LFBI

1. Lancer MSFS 2024 à LFBI au parking 6.
2. Lancer PHONIE et vérifier `DEV0.4.0.4`.
3. Régler COM1 sur Poitiers Tour.
4. Faire le premier contact avec l'indicatif complet.
5. Dire : `Fox Hôtel Novembre Novembre Yankee, prêt au roulage.`

Résultat attendu :

- aucune réponse `Aucun itinéraire accessible` ;
- une piste, un point d'attente réel et un itinéraire cohérent sont annoncés ;
- les objets immobiles des parkings voisins ne bloquent pas la ligne jaune commune ;
- l'indicatif devient ensuite `Fox Novembre Yankee`.

## 3. Diagnostic visible

Ouvrir le bouton `Diagnostic`, puis lire la section `DIAGNOSTIC ROULAGE`.

Elle doit indiquer :

- la position avion et le nœud de départ ;
- la piste analysée ;
- le nombre d'objets trafic reçus ;
- pour chaque objet : ID, indicatif, vitesse, classification, parking/segment le plus proche et blocage retenu ;
- pour chaque point d'attente : accessible ou bloqué, distance et éléments bloquants.

Pour les objets immobiles proches des parkings, la classification attendue est `PARKED_AT_STAND`. Ils peuvent bloquer leur parking et leur bretelle directe, mais pas les nœuds taxi communs.

## 4. En cas d'échec

Envoyer :

- une capture de la section `DIAGNOSTIC ROULAGE` ;
- `logs\ground-operations\ground-decisions-YYYYMMDD.jsonl` ;
- le dernier log principal `PHONIE-DEV0.4.0.4-*.log`.
