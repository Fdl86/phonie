# Test DEV0.4.0.8 - protocole succinct

1. Vérifier que GitHub Actions est entièrement vert et télécharger l'artefact à décompression unique.
2. Spawn à LFOU : LFOU, ses pistes et ses fréquences doivent remplacer immédiatement tout ancien contexte.
3. Téléportation vers LFBI : le contexte complet doit basculer vers LFBI sans redémarrer PHONIE.
4. Sur une fréquence A/A/CTAF/UNICOM : aucun dialogue PHONIE. L'interface doit afficher la Tour recommandée, ou l'Approche si aucune Tour n'existe.
5. Sur la Tour : demander le roulage. Réponse attendue : « roulez au point d'attente et rappelez prêt ».
6. À n'importe quel vrai HOLD_SHORT relié à la piste : annoncer prêt. Piste libre : alignement et autorisation de décollage. Trafic ou état inconnu : maintien.
7. En vol, régler la fréquence Approche d'un autre aérodrome : le contexte radio doit basculer sans remplacer prématurément le contexte sol.
8. Après chaque message Tour/AFIS, un PTT valide le collationnement ; sans PTT, PHONIE relance.

En cas d'anomalie : ZIP du dossier logs, capture Diagnostic, COM1 active, ICAO, position et phrase prononcée.
