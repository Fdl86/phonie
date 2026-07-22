# Changelog DEV0.4.1.3

## Correctif extraction radio SIA

- Conservation de la découverte catalogue DEV0.4.1.2, validée avec 420 aérodromes trouvés et traités.
- Abandon de l'Atlas VAC comme extracteur radio principal : certaines cartes PDF possèdent une couche texte inutilisable pour les tableaux.
- Extraction prioritaire depuis la rubrique officielle eAIP AD 2.18 en HTML.
- Repli automatique sur le PDF eAIP complet de l'aérodrome lorsque la page HTML est absente ou inutilisable.
- Conservation de l'Atlas VAC comme dernier secours uniquement.
- Lecture bornée à AD 2.18 afin d'exclure les fréquences des aides de navigation de la rubrique AD 2.19.
- Extraction du service, de l'indicatif, du canal, des horaires et des remarques de chaque ligne.
- Nom d'aérodrome lu dans AD 2.1 au lieu d'un en-tête générique.
- Diagnostics de génération séparant eAIP HTML, eAIP PDF et Atlas VAC.
- Sonde réseau rapide LFBI/LFOU ajoutée au workflow avant la génération nationale.
- Tests hors ligne ajoutés pour les deux dispositions de tableaux rencontrées : HTML et PDF.
- Validation réelle du parseur sur les PDF SIA LFBI et LFOU du cycle 07/26.

## Garde-fous préservés

- Seuil national inchangé : minimum 200 aérodromes et 150 fréquences.
- Aucune fréquence opérationnelle codée dans le code de production.
- Les ICAO de test ne servent qu'à valider le générateur contre les publications officielles.
- Moteur aérodrome DEV0.4.0.9, opérations sol, PTT, ASR, indicatifs et voix contrôleur inchangés.
