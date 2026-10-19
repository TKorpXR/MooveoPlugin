/// <summary>
/// Zone de jeu calibrée, en LECTURE seule : charge la calibration enregistrée et projette le rayon
/// d'une manette sur le mur (<see cref="CalibrationManagerBase.TryProjectRay"/>), sans rien placer
/// dans la scène. Pour un menu ou des curseurs ; un jeu qui doit positionner caméra et décor dérive
/// plutôt <see cref="CalibrationManagerBase"/> et implémente <c>ApplyCalibration</c>.
/// <code>
/// if (!playArea.LoadSaved()) { /* pas de calibration : renvoyer vers la scène de setup */ }
/// if (playArea.TryProjectRay(manette.position, manette.forward, out Vector2 uv)) { ... }
/// </code>
/// </summary>
public class MooveoPlayArea : CalibrationManagerBase
{
    protected override void ApplyCalibration() { }
}
