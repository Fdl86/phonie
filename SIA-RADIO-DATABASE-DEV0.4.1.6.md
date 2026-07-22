# Base radio SIA DEV0.4.1.6

Les jeux de données référencés par un manifest sont vérifiés sur leurs octets exacts. Git ne doit appliquer aucune conversion de fin de ligne aux fichiers concernés.

Règles de dépôt :

- `data/radio/france/** -text`
- `tests/Phonie.SmokeTests/Fixtures/radio/france/** -text`

Le workflow Windows contrôle avant le build l'attribut Git effectif, l'absence de CRLF et le SHA-256 de tous les descripteurs présents. La sérialisation C# du manifest reste en camelCase afin de conserver la compatibilité avec `validate_sia_radio.py` et le manifest publié sur GitHub.

La sélection opérationnelle conserve les services locaux et régionaux. La priorité de service prime ; la portée locale départage seulement deux services de priorité identique.
