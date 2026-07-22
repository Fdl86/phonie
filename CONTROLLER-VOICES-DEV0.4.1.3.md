# Voix contrôleur - DEV0.4.1.3

Le moteur de voix est inchangé par rapport à DEV0.4.1.2.

PHONIE inventorie les voix françaises actives installées dans Windows. Pour chaque station dialoguée, une voix est attribuée aléatoirement au premier affichage de la station pendant la session :

- répartition homme/femme lorsque les deux genres sont disponibles ;
- sélection aléatoire dans le genre retenu ;
- attribution stable pendant toute la session ;
- clé distincte par station, indicatif et type de service ;
- cache WAV séparé par station et par voix ;
- profil automatique distinct pour ATIS ;
- aucune voix attribuée aux services silencieux.

Lorsque Windows ne fournit qu'un seul genre ou aucune voix française, PHONIE utilise ce qui est disponible et l'indique dans l'interface et les logs.
