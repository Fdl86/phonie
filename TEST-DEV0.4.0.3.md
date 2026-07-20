# Protocole rapide PHONIE DEV0.4.0.3

## 1. GitHub Actions

Le workflow doit être entièrement vert :

- Build solution ;
- DEV0.3 regression smoke tests ;
- Ground Operations core tests ;
- publication `PHONIE-DEV0.4.0.3-win-x64.zip`.

## 2. Test LFBI ciblé

1. lancer MSFS à LFBI avec F-HNNY au même parking que lors du test précédent ;
2. lancer PHONIE et attendre l'affichage du graphe sol ;
3. régler COM1 sur la fréquence TOUR reconnue ;
4. effectuer le premier contact avec l'indicatif complet ;
5. dire ensuite : `Fox Novembre Yankee, prêt au roulage.`

Attendu :

- aucune réponse `Aucun itinéraire accessible` lorsque le réseau est libre ;
- point d'attente réel annoncé ;
- piste en service annoncée ;
- taxiways réels annoncés ;
- indicatif abrégé `Fox Novembre Yankee` ;
- décision `TAXI_CLEARANCE` dans `logs\ground-operations`.

## 3. Vérification occupation

Dans la ligne SOL, vérifier que l'avion utilisateur ne crée pas à lui seul plusieurs nœuds ou segments occupés.

En présence d'un vrai trafic IA proche, PHONIE peut exclure son point ou son segment. Il ne doit toutefois pas fermer arbitrairement tout le réseau de l'aérodrome.

## 4. Particularité LFBI

La clairance de roulage conduit d'abord au point d'attente. La remontée de piste ou le départ depuis une intersection est une phase distincte après l'entrée sur piste et ne doit pas être utilisée pour contourner un échec de routage taxi.

## 5. Fichiers à transmettre en cas d'échec

- dernier log principal `PHONIE-DEV0.4.0.3-*.log` ;
- `logs\ground-operations\ground-decisions-*.jsonl` ;
- dernier rapport `logs\airport-data\airport-*.json`.

Le journal de décision contient maintenant la position, le nœud de départ et la liste exacte des éléments déclarés occupés.
