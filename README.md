# 🚀 Mooveo SDK

## Système de tracking et calibration pour espaces interactifs 2D/3D

Mooveo SDK est une solution complète pour le tracking de contrôleurs et la calibration d'espaces interactifs, spécialement conçue pour les applications VR/AR et les installations interactives.

## 🛠️ Installation

### Via Unity Package Manager (Recommandé)

1. Ouvrez Unity Package Manager (`Window > Package Manager`)
2. Cliquez sur `+` → `Add package from git URL`
3. Entrez l'URL de votre repository :
   ```
   https://github.com/VOTRE_USERNAME/VOTRE_REPO.git
   ```

### Via manifest.json

Ajoutez cette ligne à votre fichier `Packages/manifest.json` :

```json
{
  "dependencies": {
    "com.bl0bli.mooveo": "https://github.com/VOTRE_USERNAME/VOTRE_REPO.git",
    "...autres dépendances..."
  }
}
```

### Configuration automatique des dépendances

Le package inclut automatiquement les dépendances requises :
- Unity Input System 1.7.0+
- Unity XR Management 4.4.0+
- Unity XR Interaction Toolkit 2.5.2+
- Unity XR OpenXR 1.10.0+
- TextMeshPro 3.0.6+
- NaughtyAttributes (via Git URL)

## 📋 Configuration requise

- **Unity 2021.3+** (recommandé : Unity 2022.3+)
- Les dépendances sont installées automatiquement avec le package

## 🚀 Démarrage rapide

### Configuration de base

```csharp
using Mooveo;

public class MyGameManager : MonoBehaviour
{
    public CalibrationManager calibrationManager;
    
    void Start()
    {
        // Initialiser le système de calibration
        calibrationManager.Init(MooveoConfigManager.Load());
        
        // Démarrer la calibration
        calibrationManager.StartCalibration();
    }
}
```

### Gestion des événements

```csharp
// Écouter les événements de calibration
calibrationManager.OnCalibrationComplete += (config) => {
    Debug.Log("Calibration terminée !");
    calibrationManager.ApplyCalibration();
};

calibrationManager.OnCalibrationFailed += (error) => {
    Debug.LogError($"Calibration échouée : {error}");
};
```

## ✨ Fonctionnalités principales

### 🎯 Système de calibration
- Calibration automatique de zones de jeu 2D/3D
- Support des contrôleurs VR et dispositifs d'entrée
- Configuration flexible des espaces interactifs
- Sauvegarde/chargement automatique des configurations

### 🎮 Gestion des contrôleurs
- Détection automatique (SteamVR, OpenXR, EOS)
- Support multi-joueurs simultané
- Suivi précis des positions et rotations
- Interface de curseur personnalisable

### 🖼️ Interface utilisateur
- Système de curseurs adaptatif
- Support Canvas World Space et Screen Space
- Compatible UI Toolkit
- Gestion des interactions utilisateur

### ⚙️ Configuration
- Paramètres globaux centralisés
- Support caméras orthographiques et perspective
- Ajustement dynamique des zones de jeu
- Compatible Unity Input System

## 📖 Architecture

### Classes principales
- **CalibrationManager** : Gestion du processus de calibration
- **MooveoDeviceManager** : Détection et gestion des dispositifs
- **CalibrationController** : Contrôleur pendant la calibration
- **CalibrationCursorUI** : Interface de curseur pour la calibration
- **GlobalSettings** : Configuration centralisée

### Structure des dossiers
```
Mooveo/
├── Runtime/
│   ├── Scripts/
│   │   ├── Calibration/     # Système de calibration
│   │   ├── Inputs/         # Gestion des contrôleurs
│   │   ├── UI/             # Interface utilisateur
│   │   └── GlobalSettings/ # Configuration globale
│   └── Resources/          # Assets par défaut
├── Editor/
│   ├── PackageBuilder.cs   # Outil de packaging
│   └── Scripts/           # Scripts d'édition
└── Samples~/              # Exemples d'utilisation
```

## 🎯 Cas d'utilisation

### 🖼️ Murs interactifs
Installations artistiques où les utilisateurs peuvent peindre ou interagir avec des surfaces projetées.

### 🎮 Applications VR/AR
Expériences de réalité virtuelle nécessitant un tracking précis des contrôleurs dans un espace défini.

### 🏛️ Musées et expositions
Installations interactives dans les espaces publics avec suivi multi-utilisateurs.

### 🎓 Applications éducatives
Simulations et environnements d'apprentissage interactifs.

## 🔧 Configuration avancée

### Personnalisation de la calibration

```csharp
var config = new MooveoConfig
{
    CalibrationPoints = 3,
    AutoSave = true,
    CalibrationMode = CalibrationMode.Surface
};

calibrationManager.Init(config);
```

### Gestion des curseurs

```csharp
var cursor = GetComponent<CalibrationCursorUI>();
cursor.Init();
cursor.SetScale(1.5f);
cursor.SetColor(Color.red);
```

## 🤝 Contribuer

1. Fork le projet
2. Créez une branche (`git checkout -b feature/NomFeature`)
3. Commitez (`git commit -m 'Ajout de la feature'`)
4. Pushez (`git push origin feature/NomFeature`)
5. Ouvrez une Pull Request

## 📄 Licence

Ce projet est sous licence MIT.

## 👨‍💻 Auteur

**Enzo Bossé**

- Email : bosseenzo6@gmail.com
- GitHub : @bl0bli

## 📞 Support

- 📧 Email : bosseenzo6@gmail.com
- 🐛 Issues : GitHub Issues

🚀 Transformez vos idées en expériences interactives avec Mooveo SDK !
