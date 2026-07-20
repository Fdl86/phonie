# PROFILS OPÉRATIONNELS - DEV0.4.0.5

Les profils complètent la géométrie SimConnect avec les appellations et fonctions publiées localement. Ils ne remplacent pas le graphe et ne contiennent pas de code.

Emplacement : `data\airports\<ICAO>.json`.

Ils décrivent l'ICAO, la révision, la source, la piste préférentielle éventuelle, la politique de phraséologie, les coordonnées des points, leur rôle et les pistes concernées.

Ordre de confiance : profil vérifié, donnée structurée cohérente, nom Facilities plausible, formulation générique.

Sans profil, PHONIE construit le graphe et calcule la route, mais n'invente pas une procédure locale. LFBI est le premier profil avec A3, A2 et A, associés géographiquement pour résister aux changements d'index ou de noms internes.
