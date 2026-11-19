using UnityEngine;

// Moves the character and nothing else: no hit, no invulnerability. The
// movement itself comes from movementSettings, as on any action - a negative
// lungeDistance for a hop away from the target, for instance.
[CreateAssetMenu(fileName = "MovementSkillAction", menuName = "Skills/Actions/Movement")]
public sealed class MovementSkillAction : SkillAction
{
}
