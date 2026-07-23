# PHONIE DEV0.4.2.0 R3 - Radio Intent & Contextual ASR

## Correctif R3

- Correction de l'échec CI de `Phonie.SmokeTests` sur `PilotIntent` et `RadioUtteranceAnalysis`.
- `Phonie.SmokeTests` référence maintenant `src/Phonie.Core/Phonie.Core.csproj`.
- Suppression des copies liées de fichiers `Phonie.Core`, qui rendaient le projet incomplet et fragile.
- Restauration et compilation de la solution complète `PHONIE.sln` en une seule étape avec `-warnaserror`.
- Ajout d'un garde-fou CI qui refuse le retour des sources Core liées dans le projet Smoke Tests.
- Suppression de tous les anciens changelogs, manifestes, notes de patch et scripts de renormalisation du ZIP source.
- Documentation actuelle regroupée dans `docs/`.

## Station et fréquence

- La fréquence active est l'autorité principale pour déterminer l'organisme dialogué.
- Le nom de la station devient facultatif, notamment après le premier contact.
- Suppression des détecteurs ouverts capables de transformer « pour un départ », « en approche » ou « avec information » en station appelée.
- Appariement exclusivement avec les stations connues de la base SIA active et du contexte courant.
- Un fragment non apparié est traité comme aucune station appelée et ne provoque jamais de silence.

## Mauvaise station explicitement appelée

- PHONIE annonce l'identifiant de la station réellement accordée.
- La fréquence cible est annoncée uniquement lorsqu'elle est officielle, unique et exploitable.
- En cas d'ambiguïté, PHONIE demande de vérifier la fréquence sans en inventer une.
- Codes : `CALLED_STATION_CORRECTED_WITH_FREQUENCY` et `CALLED_STATION_CORRECTED_CHECK_FREQUENCY`.

## Analyse canonique et intentions

- Ajout de `RadioUtteranceAnalysis` dans `Phonie.Core`.
- La même analyse alimente l'interface, les journaux et le moteur opérationnel.
- Reconnaissance renforcée de `point d'attente + départ + intersection`.
- Ajout de « retour avec vous » sans exiger le mot « de ».
- Une demande intersection peut survivre à certaines déformations du mot « prêt », avec validation ultérieure par la position réelle.

## ASR contextuel

- Prompt Whisper dynamique selon le contact, l'indicatif SimConnect, la piste, le point d'attente et les intentions plausibles.
- Station incluse dans le prompt uniquement avant l'établissement du contact.
- Activation du contexte indépendant, du segment unique, du seuil de non-parole et des probabilités.
- Rejet des PTT trop courts, trop faibles, descriptifs de bruit ou recopiant le prompt.
- Journalisation séparée du texte brut, du texte corrigé et des scores.

## Préservé

- Base radio officielle SIA sans fréquence d'aérodrome codée en dur dans la production.
- Arbitrage Facilities/SIA des canaux partagés.
- Historique de contact par organisme et remise à zéro de session de vol.
- Silence sur ATIS, messages enregistrés et auto-information.
- Stockage portable sous le dossier PHONIE.
