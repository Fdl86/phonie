# PHONIE DEV0.3.0.3 - Callsign Context

L'ATC ID du simulateur est une référence, pas un remplacement aveugle.

PHONIE accepte l'identifiant attendu lorsqu'une partie suffisante de sa forme phonétique est présente dans le bon ordre. Le nom de la station et les mots ordinaires ne sont jamais convertis en immatriculation.

Exemples acceptés pour F-HNNY :

```text
Fox Hotel November November Yankee
Fox Hôtel Novembre Novembre Yankees
Fox Hotel Novembre Bonne-Novembre et onki
Fox Hôtel Novembre deux qui
```

Exemples rejetés :

```text
Poitiers Tour
Voici tour
Fox Golf Alpha Bravo Charlie
```

Le dernier exemple correspond à un autre avion et ne doit pas être forcé vers F-HNNY.
