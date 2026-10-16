> **SDK Mooveo** — document rédigé pour GraffWall, premier jeu bâti sur le SDK : partout où il est
> question de « GraffWall », lire « le jeu ». Côté code, ce script et `SteamVRConfig` appliquent les
> mêmes règles : toute modification se reporte des deux côtés.

# SteamVR — mode « sans casque » (trackers) ↔ mode « avec casque » (PICO 4E)

En exploitation, GraffWall tourne **sans casque** : SteamVR démarre sur son pilote `null` (un faux
casque) et ne suit que les Vive Trackers. Ce réglage vient de
[SteamVRNoHeadset](https://github.com/username223/SteamVRNoHeadset).

Tant qu'il est actif, **SteamVR ignore tout casque réel** : le PICO 4 Enterprise branché par
Business Streaming n'est jamais pris comme casque, et ses manettes n'apparaissent pas. Pour tester
avec le PICO (cf. `Plan_Pico4_Test.md`), il faut remettre SteamVR sur ses réglages par défaut, puis
revenir en arrière pour les trackers.

> **Le jeu le fait désormais lui-même.** Au lancement, GraffWall met la configuration de SteamVR en
> accord avec le matériel choisi sur le poste (`SteamVRBootstrap`, cf. `CLAUDE.md` §14), en fermant
> SteamVR s'il tourne avec la mauvaise, et une vérification « Configuration SteamVR » propose de la
> corriger en cas d'échec. Ce script reste utile HORS du jeu : préparer un poste, revenir en arrière à
> la main, ou dépanner quand GraffWall ne démarre pas.

## Bascule en une commande

**SteamVR fermé** (le script refuse sinon : SteamVR réécrit son fichier de réglages en quittant et
écraserait la modification).

```powershell
# Voir l'état courant, sans rien modifier
.\tools\steamvr\Set-SteamVRProfile.ps1 -Status

# Test PICO 4 Enterprise : casque réel requis
.\tools\steamvr\Set-SteamVRProfile.ps1 -Mode Headset

# Retour production : Vive Trackers seuls, pilote null
.\tools\steamvr\Set-SteamVRProfile.ps1 -Mode NoHeadset
```

Pour vérifier une bascule : au démarrage de SteamVR, `%LOCALAPPDATA%\openvr\logs\vrcompositor.txt`
écrit « Forcing debug mode for null driver » en mode trackers, « Headset is using driver direct
mode » avec le PICO.

Chaque bascule **sauvegarde d'abord** les trois fichiers dans
`tools/steamvr/backups/<date>_avant_<mode>/`, avec un `LISEZMOI.txt` de restauration manuelle.
Ce dossier n'est pas versionné : les fichiers sont propres au poste (identifiant d'installation,
numéros de série des trackers).

## Quel fichier SteamVR lit vraiment

Le fichier UTILISATEUR est celui du dossier `config` déclaré par
`%LOCALAPPDATA%\openvr\openvrpaths.vrpath`. Sur le poste de dev, depuis l'installation de PICO
Connect (~23/09/2026), c'est **`%LOCALAPPDATA%\openvr\config\steamvr.vrsettings`**. L'ancien
`Steam\config\steamvr.vrsettings` est resté en place mais **n'est plus lu** : l'écrire n'a aucun effet.
Le script et le jeu appliquent la même règle (`SteamVRConfig.TryLocate`) ; `-Status` affiche le
fichier retenu et signale l'ancien.

⚠ Ce changement de dossier a fait perdre EN SILENCE toute la configuration kiosque de production,
restée dans l'ancien fichier (casque factice 16×16 à 60 Hz, motion smoothing coupé…). Elle est
désormais imposée explicitement (tableau ci-dessous) au lieu de dépendre d'un fichier hérité.

## Ce qui change exactement

| Fichier | Clé | NoHeadset (trackers) | Headset (PICO) |
|---|---|---|---|
| Fichier utilisateur (cf. ci-dessus) | `steamvr.requireHmd` | `false` | `true` |
| | `steamvr.forcedDriver` | `"null"` | `""` |
| | `steamvr.activateMultipleDrivers` | `true` | `false` |
| | `driver_null.enable` | `true` | `false` |
| | `driver_null.renderWidth` / `renderHeight` / `displayFrequency` | `16` / `16` / `60` | laissés en place (sans effet, le pilote est éteint) |
| | `steamvr.motionSmoothing` | `false` | retiré (défaut SteamVR) |
| | `steamvr.supersampleManualOverride` / `supersampleScale` | `true` / `0.5` | retirés (défaut SteamVR) |
| | `steamvr.hasSeenRoomSetup` | `true` | laissé en place |
| `SteamVR\drivers\null\resources\settings\default.vrsettings` | `driver_null.enable` | `true` | `false` |
| `SteamVR\resources\settings\default.vrsettings` | `requireHmd` / `forcedDriver` / `activateMultipleDrivers` | `false` / `"null"` / `true` | `true` / `""` / `false` (valeurs de Valve) |
| `chaperone_info.vrchap` (même dossier que le fichier utilisateur) | univers `2` (casque factice) | **ajouté** s'il manque, autres univers conservés | non modifié |

### La configuration de pièce — sans elle, SteamVR FERME GraffWall

Sans configuration de pièce pour l'univers du casque factice, SteamVR lève l'alerte « Configuration
de la pièce (C201) » (`Error_BaseStationUninitialized`) et vrmonitor lance l'**assistant de
configuration de la pièce** à chaque démarrage en mode trackers. Cet assistant est une application
« Scene » : SteamVR demande alors à GraffWall de quitter (`Sending Quit event to process …: GraffWallV3`
dans `vrserver.txt`), et le plugin OpenVR de Unity obéit. Vécu en build le 14/10/2026 juste après une
bascule, et le 01/10 à CHAQUE démarrage de SteamVR (le jeu était relancé à la main).

`hasSeenRoomSetup: true` ne suffit pas. Le jeu et le script ajoutent donc l'univers `2` à
`chaperone_info.vrchap`, repris à l'identique de l'ancienne configuration de production
(`Steam\config\chaperone_info.vrchap`, juillet 2025), sinon une pièce neutre (3 × 2,25 m, debout à
l'origine). En jeu, `SteamVRChaperoneGuard` journalise l'univers réellement annoncé ; s'il en
annonçait un autre, il enregistre une pièce neutre pour lui par l'API officielle (`IVRChaperoneSetup`),
valable aux démarrages suivants. Pour vérifier : `vrserver.txt` doit écrire « Found universe 2 in
chaperone file » au démarrage en mode trackers.

- ⚠ **C'est le fichier UTILISATEUR qui fait foi.** Vérifié dans les journaux : le 02/10 et le
  13/10, SteamVR a pris le PICO alors que `resources\settings\default.vrsettings` contenait les
  valeurs trackers (modifié à la main le 01/10).
- ⚠ **Pourquoi écrire quand même `resources\settings\default.vrsettings`** (étape 3 du guide
  SteamVRNoHeadset) : SteamVR RETIRE du fichier utilisateur, en le réécrivant à sa fermeture, toute
  clé égale à ces défauts (avertissement en tête du fichier de Valve). Des défauts incohérents avec
  le mode font donc disparaître nos réglages du fichier utilisateur. Ils sont écrits en dernier
  filet : un échec n'y est jamais bloquant, et une mise à jour de SteamVR qui les écrase est
  rattrapée par le fichier utilisateur.
- Le jeu compare la valeur EFFECTIVE (fichier utilisateur, sinon défauts) : une clé retirée par
  SteamVR parce qu'égale aux défauts n'est pas un écart, et ne fait pas refermer SteamVR.
- `activateMultipleDrivers: true` sert en production à charger le pilote `null` **et** le pilote
  Lighthouse des trackers en même temps. Avec un vrai casque, c'est inutile.
- Rien d'autre n'est touché : rôles des trackers (`trackers`, main gauche / main droite), tableau
  de bord, etc. Le retour en `NoHeadset` retrouve donc les trackers configurés comme avant.

## Réglages kiosque de l'ancien fichier, non repris

L'ancien `Steam\config\steamvr.vrsettings` portait aussi des réglages sans effet sur la bascule,
qui ne sont pas imposés : `steamvr.renderTargetMultiplier` (0.5), `dashboard.enableDashboard`
(false), `direct_mode.enable` (false), `notifications.DoNotDisturb` (true), `mirrorView` (0),
`perfcheck.heuristicActive` (false), `power.pauseCompositorOnStandby` (true). ⚠ Ses rôles de
trackers sont INVERSÉS par rapport au fichier en service (`LHR-6ADC5C60` main droite contre main
gauche) : à vérifier avant toute recopie.

## Restauration manuelle

Si le script ne peut pas tourner : fermer SteamVR, ouvrir le dossier de sauvegarde voulu dans
`tools/steamvr/backups/`, recopier les trois fichiers aux chemins indiqués dans son `LISEZMOI.txt`.

La sauvegarde prise avant la toute première bascule vers le PICO est
`backups/2026-09-23_13-39-07_avant_Headset/` : c'est l'état de production d'origine.

## Si SteamVR ne voit toujours pas le PICO

1. `-Status` : les clés du fichier utilisateur doivent être en mode Headset, et ce fichier doit être
   celui de `%LOCALAPPDATA%\openvr\config` (pas `Steam\config`).
2. Démarrer Business Streaming et connecter le casque **avant** d'ouvrir SteamVR (ou laisser
   Business Streaming ouvrir SteamVR lui-même).
3. Si SteamVR affiche encore « Null Model Number », il a relu un fichier réécrit entre-temps :
   fermer SteamVR, relancer `-Mode Headset`, rouvrir.

## SteamVR plante et « bloque l'extension pico » (vu le 23/09/2026)

**Symptôme** : au lancement, SteamVR signale une erreur critique, passe en mode sans échec et
désactive l'add-on `pico`. Le casque est pourtant bien vu (`Driver 'pico' started activation … 'PICO 4'`).

**Signature dans `Steam\logs\vrserver.txt`** :

```
pico: vecnlogNvEncoder::CreateEncoder : m_nvenc.nvEncInitializeEncoder(...) returned error 8
      at ...\RVRSDK_2.3.8_Source\...\VEncPlugin_Pico\...\NvEncoder.cpp:321
Exception e06d7363
Exception c0000005
Not loading driver pico because it was blocked by a previous safe mode event   (lancement suivant)
```

**Cause** : l'erreur 8 est `NV_ENC_ERR_INVALID_PARAM`. NVIDIA a **retiré les anciens préréglages
NVENC** (`NV_ENC_PRESET_*_GUID`) à partir des pilotes 582 / branche R590. L'encodeur de Business
Streaming (`VEncPlugin.dll`, RVRSDK 2.3.8) les demande encore : l'initialisation échoue, le pilote
PICO lève une exception non gérée, et SteamVR le met en quarantaine. Ce n'est lié ni aux réglages
de ce dossier ni à GraffWall : sur ce poste le pilote NVIDIA est en **610.88**.

**Remèdes, au choix** :

1. **Revenir au pilote NVIDIA 581.80** (dernier avant le retrait des préréglages). Solution
   officielle et propre, mais elle touche tout le poste. nvidia.com → recherche avancée → GeForce
   RTX 40 Series (**Notebooks**) → 581.80 → installation personnalisée, « installation propre ».
   Empêcher ensuite Windows Update de remettre un pilote récent. Pour revenir : réinstaller le
   dernier pilote.
2. **Couche de compatibilité [PicoNeoCompat](https://github.com/kakik0u/PicoNeoCompat)** (MIT,
   sources incluses). Elle intercale `nvEncCompat64.dll` entre une COPIE de `VEncPlugin.dll` et le
   pilote, et traduit les anciens préréglages. Elle garde le pilote récent, mais elle est **écrite
   pour le PICO Neo 2 / Streaming Assistant** et n'a pas été éprouvée avec le PICO 4E / Business
   Streaming. Dossier à lui indiquer : `C:\Program Files\BusinessStreaming\BusinessStreamingUW\driver`.
   Pour revenir : `Uninstall.bat`.

3. ✅ **Ce qui a résolu le problème sur ce poste** : installer la version récente de PICO Connect /
   Business Streaming (`C:\Program Files\PICO Connect\Business Streaming\`, pilote SteamVR daté de
   mai 2026), en plus du passage au pilote NVIDIA 581.80. Le nouveau pilote PICO s'enregistre seul
   (`%LOCALAPPDATA%\openvr\openvrpaths.vrpath`, `external_drivers`) et apparaît dans SteamVR comme
   `PICO_HMD`, `PICO_CONTROLLER_LEFT`, `PICO_CONTROLLER_RIGHT`. Non vérifié : si ce pilote récent
   fonctionne aussi avec un pilote NVIDIA ≥ 582, ce qui permettrait de remettre le pilote à jour.

⚠ Depuis cette installation, SteamVR écrit ses logs dans **`%LOCALAPPDATA%\openvr\logs\`** et non
plus dans `Steam\logs\` : chercher la panne dans l'ancien dossier montre une session périmée.

Après le remède : SteamVR → Paramètres → Démarrage/Arrêt → **Gérer les add-ons** → réactiver `pico`.
Sur le poste de PRODUCTION (trackers, pilote null), le problème ne se pose pas : aucun flux n'y
est encodé.
