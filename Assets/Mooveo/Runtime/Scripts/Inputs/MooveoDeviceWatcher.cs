using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>
/// Annonce l'arrivée et le départ des appareils qui peuvent jouer avec le profil matériel courant.
/// S'abonner aux événements avant <see cref="Start"/>, appeler <see cref="Dispose"/> en OnDisable.
/// </summary>
public sealed class MooveoDeviceWatcher : IDisposable
{
    private readonly Dictionary<InputDevice, bool> _devices = new Dictionary<InputDevice, bool>();
    private bool _running;

    /// <summary>Un appareil peut jouer ; vrai pour la main gauche.</summary>
    public event Action<InputDevice, bool> Joined;
    public event Action<InputDevice> Left;
    /// <summary>Contrôleur refusé par le profil matériel, avec la raison.</summary>
    public event Action<InputDevice, string> Rejected;

    /// <summary>Appareils suivis, avec leur main (vrai = gauche).</summary>
    public IReadOnlyDictionary<InputDevice, bool> Devices => _devices;
    public int Count => _devices.Count;

    /// <summary>Commence le suivi et annonce les appareils déjà branchés.</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        InputSystem.onDeviceChange += OnDeviceChange;
        foreach (InputDevice device in InputSystem.devices) TryJoin(device);
    }

    /// <summary>Arrête le suivi et oublie les appareils, sans émettre <see cref="Left"/>.</summary>
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
