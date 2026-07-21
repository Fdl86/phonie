# Voix contrôleur - DEV0.4.1.0

PHONIE inventorie les voix françaises actives installées dans Windows.

Pour chaque station dialoguée, une voix est attribuée aléatoirement au premier affichage de la station pendant la session :

- 50 % homme et 50 % femme lorsque les deux genres sont disponibles ;
- sélection aléatoire dans le genre tiré ;
- attribution stable pendant toute la session PHONIE ;
- clé distincte par station, indicatif et type de service ;
- cache WAV séparé par station et par voix ;
- ATIS avec profil automatique séparé ;
- aucune voix attribuée aux services silencieux.

Lorsque Windows ne fournit qu'un seul genre ou aucune voix française, PHONIE utilise ce qui est disponible et l'indique dans l'interface et les logs.
