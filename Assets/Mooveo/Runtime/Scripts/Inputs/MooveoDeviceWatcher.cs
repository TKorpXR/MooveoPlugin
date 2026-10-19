using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>
/// Suit les appareils qui peuvent devenir des joueurs avec le profil matériel courant
/// (<see cref="MooveoDeviceRouting.TryGetHand"/>) et annonce leurs arrivées et départs. Remplace, dans
/// un menu ou un spawner, l'abonnement à <c>InputSystem.onDeviceChange</c> et son tri.
/// <code>
/// _watcher = new MooveoDeviceWatcher();
/// _watcher.Joined += (device, isLeftHand) => …;   // brancher AVANT Start
/// _watcher.Left += device => …;
/// _watcher.Start();                               // annonce aussi les appareils déjà branchés
/// …
/// _watcher.Dispose();                             // OnDisable / OnDestroy
/// </code>
/// </summary>
public sealed class MooveoDeviceWatcher : IDisposable
{
    private readonly Dictionary<InputDevice, bool> _devices = new Dictionary<InputDevice, bool>();
    private bool _running;

    /// <summary>Un appareil peut jouer ; le booléen vaut vrai pour la main gauche.</summary>
    public event Action<InputDevice, bool> Joined;

    /// <summary>Un appareil annoncé par <see cref="Joined"/> est parti (débranché, désactivé).</summary>
    public event Action<InputDevice> Left;

    /// <summary>Un contrôleur a été écarté par le profil matériel, avec la raison (à journaliser).</summary>
    public event Action<InputDevice, string> Rejected;

    /// <summary>Appareils actuellement suivis, avec leur main (vrai = gauche).</summary>
    public IReadOnlyDictionary<InputDevice, bool> Devices => _devices;

    /// <summary>Nombre d'appareils actuellement suivis.</summary>
    public int Count => _devices.Count;

    /// <summary>Commence le suivi et annonce les appareils déjà branchés. Sans effet si déjà démarré.</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        InputSystem.onDeviceChange += OnDeviceChange;
        foreach (InputDevice device in InputSystem.devices) TryJoin(device);
    }

    /// <summary>Arrête le suivi et oublie les appareils, SANS émettre <see cref="Left"/>.</summary>
    public void Dispose()
    {
        if (!_running) return;
        _running = false;
        InputSystem.onDeviceChange -= OnDeviceChange;
        _devices.Clear();
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        switch (change)
        {
            case InputDeviceChange.Added:
            case InputDeviceChange.Reconnected:
            case InputDeviceChange.Enabled:
                TryJoin(device);
                break;
            case InputDeviceChange.Removed:
            case InputDeviceChange.Disconnected:
            case InputDeviceChange.Disabled:
                if (_devices.Remove(device)) Left?.Invoke(device);
                break;
        }
    }

    private void TryJoin(InputDevice device)
    {
        if (_devices.ContainsKey(device)) return;
        if (!MooveoDeviceRouting.TryGetHand(device, out bool isLeftHand, out string reason))
        {
            if (reason != null) Rejected?.Invoke(device, reason);
            return;
        }
        _devices.Add(device, isLeftHand);
        Joined?.Invoke(device, isLeftHand);
    }
}
