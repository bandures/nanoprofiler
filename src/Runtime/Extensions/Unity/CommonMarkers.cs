namespace Meowtron.NanoProfiler
{
    /// Marker ids for commonly used markers
    public static class CommonMarkers
    {
        // Time in milliseconds
        public const string CPU_MAIN_THREAD_FRAME_MARKER_ID = "CPU Main Thread Frame Time";
        public const string CPU_RENDER_THREAD_FRAME_MARKER_ID = "CPU Render Thread Frame Time";
        public const string GPU_FRAME_MARKER_ID = "GPU Frame Time";
        
        public const string GFX_DRAW_CALLS_COUNT_MARKER_ID = "Draw Calls Count";
        public const string GFX_TRIANGLES_COUNT_MARKER_ID = "Triangles Count";
        
        // Memory in bytes
        public const string MEMORY_TOTAL_MARKER_ID = "Total Memory";
        public const string MEMORY_GC_RESERVED_MARKER_ID = "Reserved Heap Memory";
        public const string MEMORY_GC_USED_MARKER_ID = "Used Heap Memory";

        public const string LOGIC_MODEL_UPDATE_MARKER_ID = "Model Update";
        public const string LOGIC_VIEW_UPDATE_MARKER_ID = "View Update";
        public const string LOGIC_UI_UPDATE_MARKER_ID = "UI Update";
        
        public const string STATE_GROUP = "States";
        public const string STATE_PREWARM_MARKER_ID = "Prewarm";
        public const string STATE_PREWARM_UNLOAD_MARKER_ID = "Unload Prewarm";
        public const string STATE_INIT_MARKER_ID = "Init";
        public const string STATE_TRANSITION_MARKER_ID = "Transition";
        public const string STATE_INIT_OP_MARKER_ID = "Init Operation";
        public const string STATE_ENTER_MARKER_ID = "Enter";
        public const string STATE_RUN_MARKER_ID = "Run";
        public const string STATE_EXIT_MARKER_ID = "Exit";
        public const string STATE_CALC_DOWNLOAD_ID = "Calc Download";
        
        // Download volume in bytes
        public const string DOWNLOADED_BYTES_MARKER_ID = "Downloaded Bytes";
        
        // FMOD Update CPU usage percentage on Main thread 
        public const string CPU_SOUND_UPDATE_MARKER_ID = "CPU Sound Update Usage Percent";
        // FMOD DSP thread CPU usage percentage 
        public const string CPU_SOUND_DSP_MARKER_ID = "CPU Sound DSP Usage Percent";
        // FMOD number of active playing channels 
        public const string SOUND_CHANNELS_COUNT_MARKER_ID = "Active FMOD Channels";
        // FMOD number of active voices 
        public const string SOUND_VOICES_COUNT_MARKER_ID = "Active FMOD Voices";
        
        public const string LEVEL_SCRIPT_COMPILATION_MARKER_ID = "LevelScriptCompilation";
        public const string SKILL_SCRIPT_COMPILATION_MARKER_ID = "SkillScriptCompilation";
        public const string CHARACTER_SCRIPT_COMPILATION_MARKER_ID = "CharacterScriptCompilation";
        
        public const string BATTLE_STATE_RUN_ID = "Battle";
    }
}