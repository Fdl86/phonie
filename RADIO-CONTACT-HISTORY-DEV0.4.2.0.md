# Mémoire radio et session de vol - DEV0.4.2.0

La session de vol globale conserve l'identité de l'aéronef et une collection de contacts radio. Chaque contact est indexé par la clé normalisée de l'organisme, et non par la fréquence seule.

État conservé par organisme : premier et dernier contact, indicatif complet échangé, indicatif abrégé autorisé, nombre de salutations et dernière salutation.

Un passage temporaire sur ATIS ne détruit pas le contact Tour ou SIV. Un changement Sol vers Tour ouvre en revanche un nouveau contact. Un changement d'aérodrome pendant une navigation recharge le contexte Facilities sans effacer les organismes déjà contactés.

La session complète est effacée sur chargement ou redémarrage du vol, arrêt/démarrage de la simulation, perte de connexion, changement d'appareil/indicatif, ou action manuelle « Nouvelle session ».
