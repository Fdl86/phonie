# Facilities - DEV0.4.0.7

PHONIE utilise deux flux Facilities complémentaires :

- la liste des aérodromes pour détecter automatiquement le contexte courant ;
- le rapport détaillé de chaque ICAO actif pour reconstruire pistes, fréquences, parkings, points taxi et chemins.

Les dispositions binaires MSFS 2020 et MSFS 2024 de la liste Airport sont traitées séparément. Les ICAO et coordonnées invalides sont rejetés.

Les demandes de liste ne se chevauchent pas. Une requête bloquée expire après quinze secondes. La liste proche est rafraîchie périodiquement ; la liste mondiale de secours n'est chargée qu'une fois.

Lors d'un changement d'aérodrome, un nouveau rapport Facilities est demandé même si une copie récente existe déjà en cache. Le cache sert uniquement à éviter une interface vide pendant le rechargement.
