using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Identifiant STABLE d'un matériel de suivi.
/// ⚠ Valeurs PERSISTÉES (EditorPrefs en entier, GlobalSettings.json et calibration.json par leur
/// NOM) : on ajoute TOUJOURS à la fin, on ne renomme ni ne renumérote jamais.
///
/// Ajouter un matériel = trois lignes, toutes dans ce fichier : une valeur ici, sa déclaration
/// <see cref="HardwareProfileData"/> dans <see cref="HardwareProfiles"/>, et son entrée dans
/// <see cref="HardwareProfiles.All"/>. Rien d'autre ne nomme un matériel : le choix au lancement, le
/// menu de l'éditeur et les vérifications parcourent <see cref="HardwareProfiles.All"/>.
/// </summary>
public enum HardwareProfile
{
    /// <summary>Production : Vive Trackers + stations de base, SteamVR sur le pilote null.</summary>
    ViveTrackers = 0,

    /// <summary>PICO 4 Enterprise par Business Streaming. Manettes = bombes, casque = station.</summary>
    Pico4Enterprise = 1
}

/// <summary>Axe local d'un appareil suivi.</summary>
public enum CalibrationAxis { Up, Forward }

/// <summary>
/// Ce qui diffère d'un matériel à l'autre. Tout le reste du code lit ces valeurs au lieu de
/// constantes.
///
/// ⚠ RÈGLE : le profil Vive reprend EXACTEMENT les valeurs d'avant l'introduction des profils. On ne
/// peut pas toujours tester les trackers : une valeur « approchante » ici serait une régression
/// livrée au client sans que personne ne l'ait vue.
/// </summary>
public sealed class HardwareProfileData
{
    public HardwareProfile Id;
    public string DisplayName;

    /// <summary>Une ou deux phrases pour l'opérateur qui choisit : quel matériel brancher, à quoi il sert.</summary>
    public string Description;

    /// <summary>Noms courts acceptés en ligne de commande (<c>-graffwall-hardware=…</c>), en plus du nom de l'enum.</summary>
    public string[] Aliases = Array.Empty<string>();

    /// <summary>Faux : tout ce qui est propre au test est désactivé, et le build peut partir chez un client.</summary>
    public bool IsTestProfile;

    /// <summary>
    /// Vrai si la « station » de suivi est un CASQUE (suivi inside-out) plutôt que des stations de
    /// base. Décide de ce que les vérifications de démarrage réclament : un casque suivi, ou un tracker.
    /// </summary>
    public bool StationIsHeadset;

    /// <summary>
    /// Mode SteamVR attendu sur le poste (<c>tools/steamvr/Set-SteamVRProfile.ps1 -Mode …</c>) :
    /// « NoHeadset » (pilote null, trackers seuls) ou « Headset » (vrai casque).
    /// </summary>
    public string SteamVRMode;

    // --- SteamVR -------------------------------------------------------------------------------

    /// <summary>
    /// Manifeste d'actions des trackers (<c>ViveTrackerAutoSetup</c>). Il ne déclare de binding que
    /// pour les types « vive_tracker » : chargé avec des manettes, rien ne garantit que SteamVR leur
    /// laisse l'entrée legacy qu'<c>InputReader</c> lit. Sans manifeste, l'app est en mode legacy et
    /// SteamVR applique le binding legacy fourni par le pilote de la manette.
    /// </summary>
    public bool InstallTrackerActionManifest;

    // --- Boutons (masques OpenVR legacy, VRControllerState_t.ulButtonPressed) -------------------

    public ulong TriggerMask;
    /// <summary>Poignée → événement « A » (sortie neutre, annuler une modale…).</summary>
    public ulong GripMask;
    /// <summary>Pouce → confirmer / menu. Plusieurs boutons physiques peuvent y mener.</summary>
    public ulong ThumbMask;
    /// <summary>Bouton « B » → annuler (<c>PainterController.HandleBPressed</c>). 0 = absent.</summary>
    public ulong BMask;

    /// <summary>Gâchette analogique (<c>rAxis1.x</c>) transmise telle quelle pendant l'appui.</summary>
    public bool AnalogTrigger;

    // --- Pose ----------------------------------------------------------------------------------

    /// <summary>
    /// Ré-résoudre l'index OpenVR par RÔLE (main gauche / droite) au lieu de le figer au spawn. Une
    /// manette en veille peut revenir sur un autre index.
    /// </summary>
    public bool ResolveIndexByRole;

    /// <summary>Filtrer les appareils par classe OpenVR : seule une manette peut devenir une bombe.</summary>
    public bool OnlyControllersArePainters;

    /// <summary>Appliquer <see cref="NozzleLocalPosition"/> / <see cref="NozzleLocalEuler"/> au fils « Spray ».
    /// Faux pour Vive : le prefab porte déjà le bon réglage, on n'y touche pas.</summary>
    public bool OverrideNozzle;
    public Vector3 NozzleLocalPosition;
    public Vector3 NozzleLocalEuler;

    // --- Calibration ---------------------------------------------------------------------------

    /// <summary>
    /// Axe de l'appareil relevé comme normale du mur pendant la calibration
    /// (<c>CalibrationManager.AveragePosition</c>). <c>CalibrationGraffWall</c> en tire l'ORIENTATION
    /// du mur (et le sens de la caméra) : un mauvais axe retourne le mur, et la visée part à l'opposé.
    /// Tracker plaqué contre le mur : son axe Y (Up). Manette tenue POINTÉE vers l'écran : son axe de
    /// visée (Forward), le même que le rayon du peintre.
    /// </summary>
    public CalibrationAxis CalibrationNormalAxis;

    // --- Textes --------------------------------------------------------------------------------

    /// <summary>« la bombe », « la manette ».</summary>
    public string DeviceNoun;
    /// <summary>
    /// Nom NU, en majuscule, pour un TITRE : « Bombe de peinture », « Manette ». Ne se déduit pas de
    /// <see cref="DeviceNoun"/> (retirer l'article par code supposerait un genre). Les titres qui s'en
    /// servent accordent « déconnectée »/« reconnectée » au FÉMININ : un matériel masculin demande de
    /// les revoir.
    /// </summary>
    public string DeviceName;
    /// <summary>Consigne quand le suivi est perdu.</summary>
    public string TrackingHint;
}

/// <summary>
/// Point d'accès UNIQUE au profil matériel courant.
///
/// Où vit le choix (écrit par <see cref="Select"/>, seul point d'écriture) :
/// - en ÉDITEUR, dans les EditorPrefs (propres au poste, jamais commités) — menu
///   <c>GraffWall/Matériel</c> ;
/// - en BUILD, dans <c>GlobalSettings.TrackingHardware</c> (<c>GlobalSettings.json</c> à côté de
///   l'exécutable, donc propre au poste). Vide = jamais choisi : <see cref="HasPersistedChoice"/>
///   est faux, c'est le premier lancement ;
/// - à défaut de choix mémorisé, le profil cuit au build (<c>Resources/HardwareProfileBaked.txt</c>,
///   <c>HardwareProfileBuildHook</c>), sinon Vive Trackers ;
/// - <c>-graffwall-hardware=&lt;nom ou alias&gt;</c> en ligne de commande est toujours prioritaire.
///
/// ⚠ Jamais le <c>GlobalSettings.json</c> du DÉPÔT : il est versionné et sert de source au package
/// client (<c>New-GraffwallPackage.ps1</c>). Un profil écrit là partirait chez le client.
/// </summary>
public static class HardwareProfiles
{
    public const string EditorPrefsKey = "Mooveo.HardwareProfile";
    public const string BakedResourceName = "HardwareProfileBaked";
    public const string CommandLineArgument = "-mooveo-hardware=";

    // Masques OpenVR (EVRButtonId). Écrits en clair pour ne pas faire dépendre ce fichier du plugin.
    private const int ButtonApplicationMenu = 1;   // k_EButton_ApplicationMenu — B / Y sur PICO
    private const int ButtonGrip = 2;              // k_EButton_Grip
    private const int ButtonA = 7;                 // k_EButton_A — A / X sur PICO
    private const int ButtonAxis0 = 32;            // k_EButton_SteamVR_Touchpad — clic du stick sur PICO
    private const int ButtonAxis1 = 33;            // k_EButton_SteamVR_Trigger

    private static ulong Mask(int bit) => 1ul << bit;

    /// <summary>Production. ⚠ Valeurs = constantes d'origine d'<c>InputReader</c>, ne pas « corriger ».</summary>
    public static readonly HardwareProfileData Vive = new HardwareProfileData
    {
        Id = HardwareProfile.ViveTrackers,
        DisplayName = "Vive Trackers",
        Description = "Installation de production : bombes équipées de Vive Trackers, suivies par les stations de base.",
        Aliases = new[] { "vive" },
        IsTestProfile = false,
        StationIsHeadset = false,
        SteamVRMode = "NoHeadset",
        InstallTrackerActionManifest = true,
        TriggerMask = Mask(ButtonAxis1),
        GripMask = Mask(ButtonGrip),
        ThumbMask = Mask(ButtonAxis0),
        BMask = 0,
        AnalogTrigger = false,
        ResolveIndexByRole = false,
        OnlyControllersArePainters = false,
        OverrideNozzle = false,
        CalibrationNormalAxis = CalibrationAxis.Up,
        DeviceNoun = "la bombe",
        DeviceName = "Bombe de peinture",
        TrackingHint = "ramenez la bombe face aux capteurs"
    };

    /// <summary>
    /// PICO 4 Enterprise. Masques relevés dans le binding legacy du pilote PICO
    /// (<c>openvr_driver/resources/input/legacy_bindings_pico_controller.json</c>) :
    /// gâchette → axis1, stick (clic) → axis0, poignée → grip, A/X → a, B/Y → application_menu.
    /// </summary>
    public static readonly HardwareProfileData Pico = new HardwareProfileData
    {
        Id = HardwareProfile.Pico4Enterprise,
        DisplayName = "PICO 4 Enterprise",
        Description = "Les deux manettes servent de bombes, le casque posé sur un pied sert de station.",
        Aliases = new[] { "pico" },
        IsTestProfile = false,
        StationIsHeadset = true,
        SteamVRMode = "Headset",
        InstallTrackerActionManifest = false,
        TriggerMask = Mask(ButtonAxis1),
        GripMask = Mask(ButtonGrip),
        ThumbMask = Mask(ButtonAxis0) | Mask(ButtonA),
        BMask = Mask(ButtonApplicationMenu),
        AnalogTrigger = true,
        ResolveIndexByRole = true,
        OnlyControllersArePainters = true,
        // Point de départ : la pose OpenVR d'une manette a son origine dans la poignée. À affiner
        // en Play en réglant le fils « Spray » du peintre, puis à reporter ici.
        OverrideNozzle = true,
        NozzleLocalPosition = Vector3.zero,
        NozzleLocalEuler = Vector3.zero,
        // Manette POINTÉE vers l'écran pendant la calibration : l'axe de visée, comme le peintre.
        CalibrationNormalAxis = CalibrationAxis.Forward,
        DeviceNoun = "la manette",
        DeviceName = "Manette",
        TrackingHint = "ramenez la manette dans le champ du casque"
    };

    /// <summary>
    /// Tous les profils déclarés, dans l'ordre où l'opérateur les voit. SEULE liste : le choix au
    /// lancement, le menu de l'éditeur et la lecture d'un nom passent par elle.
    /// ⚠ Déclarée APRÈS les profils : les initialiseurs statiques s'exécutent dans l'ordre du texte,
    /// placée avant elle ne contiendrait que des null.
    /// </summary>
    public static readonly IReadOnlyList<HardwareProfileData> All = new[] { Vive, Pico };

    /// <summary>Profil de PRODUCTION : celui qu'on prend quand rien n'est choisi ni lisible.</summary>
    public static HardwareProfileData Default => Vive;

    private static HardwareProfileData _current;

    /// <summary>
    /// Profil courant, résolu une fois par session (et de nouveau après <see cref="Select"/>).
    /// Lisible dès le tout premier instant du lancement, avant GlobalSettings : le choix mémorisé est
    /// alors lu directement dans le fichier (<see cref="GlobalSettingsFile"/>).
    /// </summary>
    public static HardwareProfileData Current
    {
        get
        {
            if (_current != null) return _current;
            HardwareProfileData data = Get(ReadConfigured(out bool storeReady));
            if (!storeReady) return data;
            _current = data;
            LogActive(data);
            return data;
        }
    }

    public static bool IsTest => Current.IsTestProfile;

    /// <summary>Profil déclaré pour cet identifiant ; le profil de production s'il n'est pas déclaré.</summary>
    public static HardwareProfileData Get(HardwareProfile id)
    {
        foreach (HardwareProfileData profile in All)
            if (profile.Id == id) return profile;
        Debug.LogWarning($"[HardwareProfile] Aucun profil déclaré pour « {id} » : repli sur {Default.DisplayName}.");
        return Default;
    }

    /// <summary>Vrai si un profil est déclaré pour cet identifiant (valeur persistée encore valable ?).</summary>
    public static bool IsDeclared(HardwareProfile id)
    {
        foreach (HardwareProfileData profile in All)
            if (profile.Id == id) return true;
        return false;
    }

    /// <summary>Levé après un changement de profil (<see cref="Select"/>).</summary>
    public static event Action<HardwareProfileData> Changed;

    /// <summary>Oublie le profil résolu : il sera relu au prochain accès.</summary>
    public static void Invalidate() => _current = null;

    /// <summary>
    /// Vrai si un profil a déjà été choisi et mémorisé sur ce poste. Faux au PREMIER LANCEMENT : c'est
    /// le signal qui fait proposer le choix avant les vérifications de démarrage.
    /// </summary>
    public static bool HasPersistedChoice => TryReadPersisted(out _);

    /// <summary>
    /// Choisit le profil matériel, le MÉMORISE pour les lancements suivants et le rend actif. Point
    /// d'écriture UNIQUE : premier lancement, changement en cours de partie et menu de l'éditeur.
    /// Ne relance rien : scan et calibration sont à la charge de l'appelant.
    /// </summary>
    public static void Select(HardwareProfile id)
    {
        WritePersisted(id);
        HardwareProfileData previous = _current;
        _current = Get(id);
        LogActive(_current);
        if (previous != _current) Changed?.Invoke(_current);
    }

    private static void LogActive(HardwareProfileData data)
    {
        if (data.IsTestProfile)
            Debug.LogWarning($"[HardwareProfile] Profil de TEST actif : {data.DisplayName}.");
        else
            Debug.Log($"[HardwareProfile] Profil : {data.DisplayName}");
    }

    // --- Stockage du choix ---------------------------------------------------------------------
    // ⚠ Deux magasins, jamais les deux à la fois :
    // - ÉDITEUR : EditorPrefs (propres au poste). PAS GlobalSettings.json : à la racine du projet il
    //   est VERSIONNÉ et sert de source au package client — le profil du poste de dev partirait chez
    //   le client.
    // - BUILD : GlobalSettings.json, posé à côté de l'exécutable, donc propre au poste. Le script de
    //   packaging vide la valeur : chaque poste choisit à son premier lancement.

    private static bool TryReadPersisted(out HardwareProfile id)
    {
        id = Default.Id;
#if UNITY_EDITOR
        if (!UnityEditor.EditorPrefs.HasKey(EditorPrefsKey)) return false;
        var stored = (HardwareProfile)UnityEditor.EditorPrefs.GetInt(EditorPrefsKey, (int)Default.Id);
        // Profil retiré depuis : le choix ne vaut plus, on le redemande plutôt que de deviner.
        if (!IsDeclared(stored)) return false;
        id = stored;
        return true;
#else
        // ⚠ Lu dans le FICHIER tant que GlobalSettings n'est pas chargé : le démarrage de SteamVR
        // (SteamVRBootstrap) a besoin du profil AVANT la première scène, donc avant tout MonoBehaviour.
        string stored = GlobalSettings.Core.GlobalSettings.Instance != null
            ? GlobalSettings.Core.GlobalSettings.Instance.TrackingHardware.Value
            : GlobalSettingsFile.Read("TrackingHardware");
        return TryParse(stored, out id);
#endif
    }

    private static void WritePersisted(HardwareProfile id)
    {
#if UNITY_EDITOR
        UnityEditor.EditorPrefs.SetInt(EditorPrefsKey, (int)id);
#else
        GlobalSettings.Core.GlobalSettings settings = GlobalSettings.Core.GlobalSettings.Instance;
        if (settings == null)
        {
            Debug.LogError("[HardwareProfile] GlobalSettings absent : le choix du matériel ne sera pas mémorisé.");
            return;
        }
        settings.TrackingHardware.Value = id.ToString();
        settings.SaveToJson();
#endif
    }

    /// <summary>
    /// Profil configuré. <paramref name="storeReady"/> est faux quand le magasin n'est pas encore
    /// lisible (build avant le chargement de GlobalSettings) : le résultat n'est alors qu'un défaut.
    /// </summary>
    private static HardwareProfile ReadConfigured(out bool storeReady)
    {
#if UNITY_EDITOR
        storeReady = true;
        return TryReadPersisted(out HardwareProfile chosen) ? chosen : Default.Id;
#else
        // Le choix se lit aussi dans le fichier avant le chargement de GlobalSettings : toujours lisible.
        storeReady = true;

        HardwareProfile baked = Default.Id;
        var asset = Resources.Load<TextAsset>(BakedResourceName);
        if (asset != null && !TryParse(asset.text, out baked)) baked = Default.Id;

        // Ligne de commande : prioritaire sur tout choix mémorisé, quel que soit le profil baké.
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (!arg.StartsWith(CommandLineArgument, StringComparison.OrdinalIgnoreCase)) continue;
            if (TryParse(arg.Substring(CommandLineArgument.Length), out HardwareProfile fromArgs)) return fromArgs;
        }

        // Choix mémorisé sur ce poste ; à défaut, le profil cuit au build.
        return TryReadPersisted(out HardwareProfile persisted) ? persisted : baked;
#endif
    }

    /// <summary>
    /// Lit un profil par le nom de son identifiant ou l'un de ses <see cref="HardwareProfileData.Aliases"/>.
    /// Faux pour un texte vide, inconnu, ou désignant un profil qui n'est plus déclaré ;
    /// <paramref name="profile"/> vaut alors le profil de production.
    /// </summary>
    public static bool TryParse(string text, out HardwareProfile profile)
    {
        profile = Default.Id;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim();

        foreach (HardwareProfileData data in All)
        {
            if (data.Id.ToString().Equals(t, StringComparison.OrdinalIgnoreCase)) { profile = data.Id; return true; }
            foreach (string alias in data.Aliases)
                if (alias.Equals(t, StringComparison.OrdinalIgnoreCase)) { profile = data.Id; return true; }
        }
        return false;
    }
}
