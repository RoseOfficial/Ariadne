using FFXIVClientStructs.FFXIV.Client.UI;

namespace Ariadne.Movement;

/// <summary>
/// Prevents AFK timer from triggering during automated movement.
/// </summary>
internal static unsafe class OverrideAFK
{
    public static void ResetTimers()
    {
        var module = UIModule.Instance()->GetInputTimerModule();
        module->AfkTimer = 0;
        module->ContentInputTimer = 0;
        module->InputTimer = 0;
    }
}
