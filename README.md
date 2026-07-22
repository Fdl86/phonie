# PHONIE DEV0.4.1.6 - SHA-256 INTEGRITY & RADIO CORRECTNESS

DEV0.4.1.6 ferme la cause racine du smoke test Windows : Git ne peut plus convertir en CRLF les jeux de données dont le SHA-256 est déclaré dans un manifest.

Cette version corrige également la recommandation des services régionaux, la sérialisation camelCase du manifest, la bascule AIRAC en mémoire sur dossier non inscriptible et les chemins de cache voix incompatibles avec Windows.

Le workflow compile et exécute tous les tests avant la génération réseau. Il vérifie désormais les attributs Git, les fins de ligne et chaque SHA-256 avec un diagnostic explicite.

Commencer par `TEST-DEV0.4.1.6.md`.

Après extraction dans le dépôt en conservant `.git`, exécuter une fois `RENORMALIZE-GIT-INDEX-DEV0.4.1.6.cmd`, puis vérifier et pousser avec GitHub Desktop. Le script refuse de continuer si un attribut Git, une fin de ligne ou un SHA-256 radio n’est pas exact.
