# PHONIE DEV0.3.0.5 - Profil Turbo Vulkan

Profil : `Whisper Large-v3 Turbo Vulkan - qualité`.

Modèle : `ggml-large-v3-turbo-q5_0.bin`, environ 548 Mio.

Le téléchargement reste à la demande et vérifié par SHA-256. Lorsque le profil est actif, DEV0.3.0.5 charge le modèle et effectue une courte inférence silencieuse en arrière-plan. Cette opération initialise Vulkan avant le premier appel pilote réel.

Le profil est compatible avec les pilotes Vulkan AMD, NVIDIA et Intel. Le runtime conserve un fallback CPU explicite. Le benchmark indique si une activité GPU du processus a réellement été observée.
