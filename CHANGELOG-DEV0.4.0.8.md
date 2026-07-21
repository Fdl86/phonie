# Changelog DEV0.4.0.8

- consolidation de la détection dynamique de tout aérodrome et du rechargement complet de son contexte ;
- séparation maintenue entre contexte géographique et contexte radio ;
- A/A, CTAF, UNICOM, ATIS et météo automatique toujours silencieux, même si le type COM SimConnect est ambigu ;
- fréquence dialoguée recommandée selon l'ordre Tour, Sol/Clairance, Approche/Départ, AFIS/FSS ;
- sur un aérodrome avec A/A et Tour/Approche, PHONIE indique la Tour en priorité sans modifier automatiquement COM1 ;
- tout vrai point HOLD_SHORT Facilities reste valable pour la séquence de départ ;
- suppression du dernier test LFBI dépendant d'une entrée locale non associée ;
- ajout de tests automatiques sur la priorité Tour, le repli Approche et le silence A/A.
