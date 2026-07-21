# Changelog DEV0.4.0.9

- correction du décodeur `SIMCONNECT_RECV_AIRPORT_LIST` pour MSFS 2024 ;
- prise en charge contrôlée de l'emplacement de compatibilité supplémentaire observé avec SimConnect.NET 0.2.1 ;
- ajout de tests synthétiques reproduisant exactement les charges utiles observées : 5832 octets pour 161 éléments et 7380 octets pour 204 éléments, avec un emplacement résiduel volontairement valide qui doit être ignoré ;
- suppression du calcul erroné du stride par division directe de la charge utile par `dwArraySize` ;
- ajout du catalogue portable `data/radio/france-official.json` ;
- priorité des fréquences officielles vérifiées sur les fréquences Facilities de scène pour LFBI et LFOU ;
- gestion des horaires AFIS publiés de LFOU et bascule A/A silencieuse hors horaires ;
- recommandation LFBI Tour 118.505 sans dépendre du contenu radio de MSFS ;
- maintien de 118.500 uniquement comme secours de compatibilité MSFS 2020, jamais comme recommandation officielle ;
- rechargement géographique et radio conservé pour tout nouvel aérodrome détecté ;
- aucune modification automatique de COM1.

- le catalogue officiel de cette candidate est volontairement limité à LFBI et LFOU ; les autres terrains conservent un repli Facilities prudent.
