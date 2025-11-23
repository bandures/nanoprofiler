using System;

namespace Meowtron.NanoProfiler
{
    [Flags]
    public enum NanoMarkerFlags : ushort
    {
        Default = 0,
        // Don't replicate Marker to Unity profiler
        DontReflectToUnity = 1 << 0,
        // Removed
        RemoveFlag1 = 1 << 1,
        // Marker was pre-created from settings. Pre-created markers don't know final
        // flags and need to be updated later when client codes attempt to create them.
        PreCreated = 1 << 2,
        
        // All colors bits mask
        ColorsMask = 0xF000,
        // Paint Marker "red" in visualization tools that support colors 
        ColorRed = 0x1000,
        // Paint Marker "cyan" in visualization tools that support colors 
        ColorCyan = 0x2000,
        // Paint Marker "yellow" in visualization tools that support colors 
        ColorYellow = 0x3000,
        // Paint Marker "purple" in visualization tools that support colors 
        ColorPurple = 0x4000,
        // Paint Marker "gray" in visualization tools that support colors 
        ColorGray = 0x5000,
    }
}