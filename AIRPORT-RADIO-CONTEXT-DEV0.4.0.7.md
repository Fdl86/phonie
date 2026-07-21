# Contextes aérodrome et radio - DEV0.4.0.7

## Deux contextes indépendants

`GeographicAirportIcao` représente l'aérodrome correspondant à la position réelle de l'avion. Il pilote le graphe sol, la localisation, le trafic au sol et la carte Diagnostic.

`RadioAirportIcao` représente l'aérodrome ou la station ciblée par COM1. Il pilote la classification du service, les fréquences associées et le dialogue radio.

Ces deux ICAO peuvent être différents en vol.

## Détection géographique

PHONIE demande périodiquement à SimConnect la liste des aérodromes disponibles dans le cache Facilities proche. En cas d'absence de l'interface étendue, une liste mondiale est chargée une seule fois comme secours.

Le terrain géographique est sélectionné selon la distance réelle, avec une petite hystérésis pour éviter les oscillations entre terrains proches. Une téléportation force une actualisation immédiate.

Au sol, lorsque la liste proche n'est pas encore disponible, l'identifiant et la position de la station COM peuvent servir de secours uniquement si la station est réellement proche de l'avion.

## Résolution radio

Priorité :

1. identifiant ICAO fourni par la station COM active ;
2. position calculée depuis le relèvement et la distance de la station COM ;
3. correspondance de fréquence dans les rapports Facilities déjà chargés ;
4. contexte radio non résolu, sans réponse inventée.

## Changement de contexte

Un changement géographique invalide le moteur sol précédent, sa piste, son itinéraire, ses points d'attente, son trafic et sa carte.

Un changement radio invalide la classification de station et le cache ATIS vocal précédent.

Les Facilities du nouvel ICAO géographique et du nouvel ICAO radio sont redemandées automatiquement. Les rapports récents peuvent être utilisés immédiatement en attendant la réponse fraîche.
