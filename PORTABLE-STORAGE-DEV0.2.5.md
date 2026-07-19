# Stockage portable - DEV0.2.5

PHONIE utilise `AppContext.BaseDirectory` comme racine unique de ses propres données.

Répertoires :

- `config` pour les réglages JSON ;
- `logs` pour les diagnostics ;
- `logs\airport-data` pour les rapports JSON et TXT des aérodromes ;
- `recordings` pour le dernier PTT ;
- `cache` pour les futurs caches vocaux et ATIS.

Aucun mécanisme de repli vers AppData n'est prévu. Si la racine n'est pas inscriptible, l'application s'arrête avec un message explicite.

Cela implique de ne pas placer PHONIE dans un dossier protégé comme `C:\Program Files` sans droits d'écriture. Un dossier utilisateur dédié est recommandé.
