/// <summary>
/// Zone de jeu calibrée en lecture seule (menus, curseurs) : <c>LoadSaved()</c> puis <c>TryProjectRay(...)</c>.
/// Pour placer caméra et décor, dériver plutôt <see cref="CalibrationManagerBase"/>.
/// </summary>
public class MooveoPlayArea : CalibrationManagerBase
{
    protected override void ApplyCalibration() { }
}
