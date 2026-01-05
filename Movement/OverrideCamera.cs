using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using System;
using System.Runtime.InteropServices;

namespace Ariadne.Movement;

[StructLayout(LayoutKind.Explicit, Size = 0x2B0)]
public unsafe struct CameraEx
{
    [FieldOffset(0x130)] public float DirH; // 0 is north, increases CW
    [FieldOffset(0x134)] public float DirV; // 0 is horizontal, positive is looking up, negative looking down
    [FieldOffset(0x138)] public float InputDeltaHAdjusted;
    [FieldOffset(0x13C)] public float InputDeltaVAdjusted;
    [FieldOffset(0x140)] public float InputDeltaH;
    [FieldOffset(0x144)] public float InputDeltaV;
    [FieldOffset(0x148)] public float DirVMin; // -85deg by default
    [FieldOffset(0x14C)] public float DirVMax; // +45deg by default
}

/// <summary>
/// Hooks camera input to automatically align camera with movement direction.
/// </summary>
public unsafe class OverrideCamera : IDisposable
{
    public bool Enabled
    {
        get => _rmiCameraHook.IsEnabled;
        set
        {
            if (value)
                _rmiCameraHook.Enable();
            else
                _rmiCameraHook.Disable();
        }
    }

    /// <summary>
    /// If true, override even if user tries to change camera orientation.
    /// Otherwise, override only if user does nothing.
    /// </summary>
    public bool IgnoreUserInput;

    /// <summary>
    /// Desired horizontal camera angle.
    /// </summary>
    public Angle DesiredAzimuth;

    /// <summary>
    /// Desired vertical camera angle.
    /// </summary>
    public Angle DesiredAltitude;

    /// <summary>
    /// Horizontal rotation speed (per second).
    /// </summary>
    public Angle SpeedH = 360.Degrees();

    /// <summary>
    /// Vertical rotation speed (per second).
    /// </summary>
    public Angle SpeedV = 360.Degrees();

    private delegate void RMICameraDelegate(CameraEx* self, int inputMode, float speedH, float speedV);
    [Signature("E8 ?? ?? ?? ?? EB 05 E8 ?? ?? ?? ?? 44 0F 28 4C 24 ??")]
    private Hook<RMICameraDelegate> _rmiCameraHook = null!;

    public OverrideCamera()
    {
        Services.GameInterop.InitializeFromAttributes(this);
        Services.Log.Information($"RMICamera address: 0x{_rmiCameraHook.Address:X}");
    }

    public void Dispose()
    {
        _rmiCameraHook.Dispose();
    }

    private void RMICameraDetour(CameraEx* self, int inputMode, float speedH, float speedV)
    {
        _rmiCameraHook.Original(self, inputMode, speedH, speedV);
        if (IgnoreUserInput || inputMode == 0)
        {
            var dt = Framework.Instance()->FrameDeltaTime;
            var deltaH = (DesiredAzimuth - self->DirH.Radians()).Normalized();
            var deltaV = (DesiredAltitude - self->DirV.Radians()).Normalized();
            var maxH = SpeedH.Rad * dt;
            var maxV = SpeedV.Rad * dt;
            self->InputDeltaH = Math.Clamp(deltaH.Rad, -maxH, maxH);
            self->InputDeltaV = Math.Clamp(deltaV.Rad, -maxV, maxV);
        }
    }
}
