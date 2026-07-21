# CHANGELOG - PHONIE DEV0.4.0.6

## HOLD SHORT FLOW

- remplacement des noms locaux de points dans la clairance de roulage par la phrase générique `roulez au point d'attente et rappelez prêt` ;
- conservation du calcul complet du trajet, du point choisi et des appellations dans le diagnostic ;
- suppression de l'influence du point annoncé par le pilote sur la décision de départ ;
- validation du départ par la position géométrique réelle sur un point d'attente lié à la piste attribuée ;
- exclusion explicite des points intermédiaires, notamment A3 à LFBI ;
- clairance Tour combinée alignement et décollage lorsque le point d'attente est confirmé et la piste libre ;
- maintien obligatoire si la piste est occupée ;
- maintien obligatoire si l'état du trafic est inconnu ;
- AFIS maintenu en mode strictement informatif ;
- noms A, A2, A3, D1 et intersection retirés des réponses opérationnelles normales ;
- tests ajoutés pour la phrase générique, le point intermédiaire, le nom annoncé erroné, la piste occupée, le trafic inconnu et l'AFIS au point d'attente ;
- version visible et artefact mis à jour vers DEV0.4.0.6 ;
- livraison à décompression unique conservée.

## Limite actuelle du trafic

DEV0.4.0.6 contrôle l'occupation de la piste et le trafic au sol fourni au moteur Ground Operations. La détection et le séquencement avancés des avions en circuit ou en finale seront traités dans DEV0.6 - Traffic & Sequencing.
