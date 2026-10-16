# Changelog — Mooveo SDK

## [1.2.0] — 2026-10-16

Synchronisation avec le système de GraffWall V3 : tout ce qui relève du système (calibration,
tracking, profils matériel / SteamVR, vérifications de démarrage, réparations, notifications,
popups) fait désormais partie du SDK.

### Ajouté
- **Profil matériel** (`Hardware/`) : Vive Trackers ou PICO 4 Enterprise, choisi au premier
  lancement, mémorisé par poste, modifiable en calibration (bouton « Matériel ») et en jeu.
  Point d'accès unique : `HardwareProfiles.Current`.
- **Configuration SteamVR automatique** selon le profil (`SteamVRConfig`, `SteamVRBootstrap`,
  `SteamVRChaperoneGuard`), avec sauvegarde préalable. Script équivalent : `Tools~/steamvr`.
- **Vérifications de démarrage** (`SystemCheck/`) : périphériques, calibration, profil matériel,
  SteamVR, HotFolderPrint, EOS Utility, dossiers, disque, build. Une vérification = une classe
  implémentant `ISystemCheck` : celles déclarées par le JEU sont découvertes automatiquement.
- **Réparations** (« Résoudre ») : localisation de logiciels, création de dossiers, réécriture de
  configuration SteamVR.
- **Notifications globales** (`NotificationCenter`), journal d'erreurs persistant, popups de choix
  (`ChoicePopup`) et guides illustrés (`GuidePopup`).
- **Pointage UI** : souris sur tous les panneaux (`MouseUIBridge`), panneaux d'écran
  (`ScreenSpaceUI`), défilement tactile.
- **`MooveoGameHooks`** : points d'extension du SDK vers le jeu (hôte des popups, curseurs
  redessinés sur les notifications, panneaux de joueur, modales du jeu, surface de jeu, nom de
  l'application). Rien n'est obligatoire : sans branchement, le comportement est celui d'une
  scène de calibration.
- Éditeur : fenêtre **Mooveo/Matériel…** et hook de build qui cuit le profil choisi.

### Modifié
- `GlobalSettings` : réglages **système** uniquement, fichier `GlobalSettings.json` à la racine de
  l'application (éditeur et build), chargement « Smart Merge » (le fichier est complété puis
  réécrit à chaque lancement). Nouveaux réglages : `TrackingHardware`, `LogsPath`,
  `CameraPicturesPath`, `DefaultPrintPath`, `DNPBanner*`. Extensible par héritage
  (`ApplyDefaultPaths`, `RequiredDirectories`, `ForceNotifyAll`, `LoadSettings` virtuels).
- Écran de calibration : bouton « Commencer » (test souris en mode Admin), liste des
  vérifications défilante, textes en français, nouvelle feuille de style.
- `DefaultController` : un `ControllerShortcutHandler` non renseigné n'est plus une erreur.

### Corrigé
- Les seuils de linéarité / symétrie réglés à l'inspecteur sont enfin appliqués.
- Calibration affinée (9 points) persistée en 3 points moyennés par colonne.

### Migration depuis 1.1.x
- ⚠ **Emplacement des réglages** : `StreamingAssets/global_settings.json` n'est plus lu. Les
  réglages vivent dans `GlobalSettings.json` à la racine du projet (éditeur) ou deux niveaux
  au-dessus de `<Jeu>_Data` (build). Recopier ses valeurs si besoin ; le fichier est créé au
  premier lancement.
- `GlobalSettings` détruit désormais le GameObject en double et passe en `DontDestroyOnLoad`.
- Ligne de commande : `-mooveo-hardware=vive|pico` force le profil matériel.
- Unity **6000.0** minimum.
- API utilisées par les jeux existants (`DefaultController`, `InputReader.SetInputConfig`,
  `MooveoDeviceManager.SetupPlayer/RemovePlayer`, `CalibrationManagerBase.ApplyCalibration`) :
  inchangées.

## [1.1.0] — 2026-06-15
- Alignement sur le dossier Mooveo de GraffWall V3 (mai–juin 2026) : mode Admin, verrouillage
  rotation / taille du curseur, feedback d'erreurs de calibration, `VirtualMouseDriver`, `UIToast`.
