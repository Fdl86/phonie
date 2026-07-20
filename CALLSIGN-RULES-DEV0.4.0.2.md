# Indicatifs - PHONIE DEV0.4.0.2

L'ATC ID fourni par SimConnect reste la seule identité avion de confiance.

## Premier contact

L'indicatif est prononcé en entier. Exemple :

`F-HNNY` devient `Fox Hôtel Novembre Novembre Yankee`.

## Après établissement du contact

PHONIE peut employer l'abréviation constituée de la marque de nationalité et des deux derniers caractères :

`F-HNNY` devient `F-NY`, prononcé `Fox Novembre Yankee`.

L'abréviation n'est autorisée qu'après une première réponse ayant utilisé l'indicatif complet. La session conserve simultanément :

- `FullCallsign = F-HNNY` ;
- `AuthorizedShortCallsign = F-NY` ;
- `ContactEstablished = true`.

Les mots reconnus dans le message du pilote ne peuvent pas remplacer l'ATC ID ni créer un nouvel indicatif.
