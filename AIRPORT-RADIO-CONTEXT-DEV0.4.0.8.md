# Contextes aérodrome et radio - DEV0.4.0.8

Le contexte géographique suit automatiquement l'aérodrome où se trouve l'avion. Chaque changement recharge Facilities, pistes, fréquences, météo, réseau sol et diagnostic.

Le contexte radio suit la fréquence COM active et peut viser un autre aérodrome en vol. PHONIE respecte toujours la fréquence réellement réglée : il ne change jamais COM1 automatiquement.

Règles radio :

1. Tour prioritaire lorsqu'elle existe ;
2. Sol ou Clairance lorsque pertinent ;
3. Approche ou Départ ;
4. AFIS/FSS ;
5. A/A, CTAF, UNICOM, ATIS et AWS restent silencieux.

Si COM1 est sur A/A alors qu'une Tour existe, PHONIE ne répond pas et affiche la fréquence Tour recommandée. Une classification Facilities silencieuse prime sur un type COM SimConnect ambigu afin d'empêcher toute réponse accidentelle sur A/A.
