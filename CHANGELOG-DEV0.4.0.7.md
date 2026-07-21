# Changelog DEV0.4.0.7

- détection automatique de tout aérodrome après spawn, déplacement ou téléportation ;
- rechargement complet du contexte géographique : Facilities, fréquences, pistes, réseau sol, trafic et carte ;
- séparation du contexte géographique et du contexte radio ;
- résolution de l'aérodrome radio par identifiant COM, position de station puis fréquence Facilities ;
- chargement automatique des Facilities de l'aérodrome d'approche en vol ;
- suppression de toute dépendance opérationnelle à A2 ou à un rôle de profil local ;
- tout véritable `HOLD_SHORT` Facilities devient valable pour l'annonce prêt au départ ;
- repli de routage sur tous les points d'attente quand les associations de piste de la scène sont absentes ;
- tolérance de localisation des points d'attente portée à 55 m ;
- interface et bouton de rechargement rendus génériques ;
- nouveaux tests de sélection géographique, téléportation, contexte radio distant et repli Facilities.
