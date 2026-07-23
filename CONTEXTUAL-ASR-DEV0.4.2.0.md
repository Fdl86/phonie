# ASR contextuel - DEV0.4.2.0

Le prompt Whisper est reconstruit pour chaque PTT depuis le contexte opérationnel. La station n'est incluse qu'avant l'établissement du contact. L'indicatif SimConnect, la piste, le point d'attente et les expressions probables sont ajoutés lorsqu'ils sont disponibles.

Garde-fous :

- durée minimale 180 ms ;
- niveau minimal -62 dBFS ;
- seuil Whisper de non-parole ;
- segment unique et absence de contexte hérité ;
- rejet des descriptions de bruit ;
- rejet des sorties ressemblant fortement au prompt.

Les seuils sont des valeurs de départ destinées à être affinées avec les enregistrements réels. Les diagnostics conservent le brut et le texte nettoyé afin de rendre chaque décision auditée et réversible.
