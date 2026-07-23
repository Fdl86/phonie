# PHONIE DEV0.4.2.0 - RADIO INTENT & CONTEXTUAL ASR

Cette version corrige les faux silences observés en vol réel et rend les transmissions naturelles après le premier contact.

Principaux changements :

- la fréquence active reste la référence principale ; le nom de la station n'est plus requis après contact ;
- les stations appelées sont reconnues uniquement dans l'ensemble fermé issu de la base SIA active ;
- « pour un départ », « en approche » ou « avec information » ne peuvent plus devenir des stations fantômes ;
- lorsqu'une autre station connue est appelée, PHONIE indique la station réellement accordée et fournit la fréquence cible seulement lorsqu'elle est officielle, unique et exploitable ;
- une seule analyse canonique alimente l'interface, les journaux et le moteur opérationnel ;
- les demandes de départ intersection survivent à certaines déformations du mot « prêt », sous contrôle de la position réelle ;
- Whisper reçoit un prompt court et dynamique construit depuis l'indicatif, la phase de vol, la piste et le point d'attente ;
- les PTT vides, trop faibles, descriptifs de bruit ou reproduisant le prompt sont rejetés ;
- les transcriptions brute et nettoyée, les scores et les corrections sont journalisés séparément ;
- les 26 transmissions de la session DEV0.4.1.9 sont intégrées comme corpus de non-régression.

Commencer par `TEST-DEV0.4.2.0.md`.

Après extraction à la racine du dépôt en conservant `.git`, exécuter `RENORMALIZE-GIT-INDEX-DEV0.4.2.0.cmd`, puis vérifier GitHub Desktop avant le commit.

### Extraction propre et renormalisation Git

La révision R2 de DEV0.4.2.0 doit être extraite à la racine du dépôt en conservant uniquement le dossier `.git`. Le script `RENORMALIZE-GIT-INDEX-DEV0.4.2.0.cmd` enregistre d'abord les ajouts et suppressions du nouveau ZIP, puis renormalise les fichiers suivis. Cette séquence évite l'erreur `unable to stat` sur un ancien document suivi mais absent de la nouvelle source.
