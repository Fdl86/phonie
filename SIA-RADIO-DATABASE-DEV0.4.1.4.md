# Base radio SIA - DEV0.4.1.4

## Cause de l'échec précédent

DEV0.4.1.2 a correctement découvert 420 documents Atlas VAC. La génération a toutefois produit seulement 113 fréquences, car de nombreuses cartes utilisent des polices PDF internes que l'extracteur texte ne peut pas restituer correctement. La limite de sécurité de 150 fréquences a donc bloqué le workflow, comme prévu.

## Chaîne de données

Pour les aérodromes français, PHONIE utilise exclusivement des publications officielles du Service de l'Information Aéronautique :

1. export XML/AIXM 4.5 lorsqu'il est directement accessible ;
2. catalogue Atlas VAC pour découvrir la liste des ICAO du cycle ;
3. page HTML eAIP officielle de chaque aérodrome, rubrique AD 2.18 ;
4. PDF eAIP complet AD 2 en repli ;
5. carte Atlas VAC en dernier secours.

La rubrique AD 2.18 contient les moyens de radiocommunication ATS. Le parseur s'arrête avant AD 2.19 pour ne pas confondre VOR, DME, ILS ou autres aides de navigation avec une fréquence de dialogue.

## Données extraites

Chaque ligne conserve :

- canal et fréquence porteuse normalisée ;
- type de service ;
- indicatif français ;
- portée locale ou régionale ;
- caractère dialogué ou silencieux ;
- code horaire publié ;
- remarques ;
- URL et identifiant de la publication source ;
- cycle AIRAC, dates d'effet, révision et SHA-256 du jeu final.

## Structure portable

```text
PHONIE/data/radio/france/
  manifest.json
  previous/airports-fr.json
  current/airports-fr.json
  next/airports-fr.json
  .staging/
```

`previous` est le seul secours autorisé. Facilities MSFS ne remplace jamais une donnée radio française officielle absente ou invalide.

## Garde-fous

La génération est refusée si elle produit moins de 200 aérodromes ou moins de 150 fréquences. Ce seuil n'est pas diminué pour faire passer artificiellement le workflow.

PHONIE reste silencieux pour A/A, CTAF, UNICOM, ATIS, AWS et message enregistré. Les horaires HX, PPR ou dépendants d'un NOTAM ne sont pas inventés.
