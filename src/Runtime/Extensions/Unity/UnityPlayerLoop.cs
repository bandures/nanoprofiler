#if UNITY_2022_3_OR_NEWER
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;

namespace Meowtron.NanoProfiler
{
    /// <summary>
    /// NanoProfiler service which adds Unity Player Loop
    /// stages  to the profiler stream.
    /// </summary>
    public sealed class UnityPlayerLoop : INanoProfilerService
    {
        /// Helper class to encapsulate all required SPL integration objects
        readonly struct SystemMarker
        {
            public readonly NanoScope Scope;

            public SystemMarker(NanoProfiler profiler, string name)
            {
                Scope = new(profiler, name, NanoProfiler.INVALID_ID, NanoMarkerFlags.DontReflectToUnity);
            }

            public void Apply<T>(List<PlayerLoopSystem> systemsList)
            {
                UnityPlayerLoopTools.InsertBefore(systemsList, Begin(), typeof(T));
                UnityPlayerLoopTools.InsertAfter(systemsList, End(), typeof(T));
            }
            
            public PlayerLoopSystem Begin() => new() { type = typeof(UnityPlayerLoop), updateDelegate = Scope.Begin };
            public PlayerLoopSystem End() => new() { type = typeof(UnityPlayerLoop), updateDelegate = Scope.End };
        }

        private readonly NanoProfilerProxy _profiler;
        private readonly SystemMarker _frameMarker;
        private readonly SystemMarker _fixedUpdateMarker;
        private readonly SystemMarker _earlyUpdateMarker;
        private readonly SystemMarker _preUpdateMarker;
        private readonly SystemMarker _updateMarker;
        private readonly SystemMarker _preLateUpdateMarker;
        private readonly SystemMarker _animationLateUpdateMarker;
        private readonly SystemMarker _particlesLateUpdateMarker;
        private readonly SystemMarker _physicsLateUpdateMarker;
        private readonly SystemMarker _scriptLateUpdateMarker;
        private readonly SystemMarker _postLateUpdateMarker;
        private readonly SystemMarker _uGuiUpdateMarker;
        private readonly SystemMarker _particleSystemEndUpdateMarker;
        private readonly SystemMarker _renderAndPresentMarker;
        private readonly SystemMarker _srpRenderFrameMarker;

        private void GlobalOnFrameEnd() => _profiler.MarkNewFrame(); 
        private void SrpBeginFrameRendering(ScriptableRenderContext ctx, List<Camera> cameras) => _srpRenderFrameMarker.Scope.Begin();
        private void SrpEndFrameRendering(ScriptableRenderContext ctx, List<Camera> cameras) => _srpRenderFrameMarker.Scope.End();

        public UnityPlayerLoop(NanoProfiler profiler)
        {
            _profiler = profiler.Proxy;
            
            _frameMarker = new(profiler, "Frame");
            _fixedUpdateMarker = new(profiler, "FixedUpdate");
            _earlyUpdateMarker = new(profiler, "EarlyUpdate");
            _preUpdateMarker = new(profiler, "PreUpdate");
            _updateMarker = new(profiler, "Update");
            _preLateUpdateMarker = new(profiler, "PreLateUpdate");
            _animationLateUpdateMarker = new(profiler, "Animation.LateUpdate");
            _particlesLateUpdateMarker = new(profiler, "Particles.LateUpdate");
            _physicsLateUpdateMarker = new(profiler, "Physics.LateUpdate");
            _scriptLateUpdateMarker = new(profiler, "Script.LateUpdate");
            _postLateUpdateMarker = new(profiler, "PostLateUpdate");
            _uGuiUpdateMarker = new(profiler, "UGUI.Update");
            _particleSystemEndUpdateMarker = new(profiler, "ParticleSystemEndUpdate");
            _renderAndPresentMarker = new(profiler, "RenderAndPresent");
            _srpRenderFrameMarker = new(profiler, "SRP.Render");
        }

        /// <summary>
        /// Installs callbacks into SPL, should be called once.
        /// In Editor needs to be paired with RemovePlayerLoopCallbacks
        /// because Editor doesn't reset SPL state between play runs.
        /// </summary>
        public void Start()
        {
            var globalOnFrameEndSystem = new PlayerLoopSystem() { type = typeof(UnityPlayerLoop), updateDelegate = GlobalOnFrameEnd };
            
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            UnityPlayerLoopTools.UpdateSubSystems(ref playerLoop, (systemsList) =>
            {
                _fixedUpdateMarker.Apply<FixedUpdate>(systemsList);
                _updateMarker.Apply<Update>(systemsList);
                _earlyUpdateMarker.Apply<EarlyUpdate>(systemsList);
                _preUpdateMarker.Apply<PreUpdate>(systemsList);
                _preLateUpdateMarker.Apply<PreLateUpdate>(systemsList);
                _postLateUpdateMarker.Apply<PostLateUpdate>(systemsList);
                
                systemsList.Insert(0, _frameMarker.Begin());
                systemsList.Add(_frameMarker.End());
                systemsList.Add(globalOnFrameEndSystem);
                
            });

            ref var preLateUpdate = ref UnityPlayerLoopTools.FindSystem(playerLoop, typeof(PreLateUpdate));
            UnityPlayerLoopTools.UpdateSubSystems(ref preLateUpdate, (systemsList) =>
            {
                UnityPlayerLoopTools.InsertBefore(systemsList, _animationLateUpdateMarker.Begin(), typeof(PreLateUpdate.DirectorUpdateAnimationBegin));
                UnityPlayerLoopTools.InsertAfter(systemsList, _animationLateUpdateMarker.End(), typeof(PreLateUpdate.DirectorUpdateAnimationEnd));
                _particlesLateUpdateMarker.Apply<PreLateUpdate.ParticleSystemBeginUpdateAll>(systemsList);
                _physicsLateUpdateMarker.Apply<PreLateUpdate.PhysicsLateUpdate>(systemsList);
                _scriptLateUpdateMarker.Apply<PreLateUpdate.ScriptRunBehaviourLateUpdate>(systemsList);
            });
            
            ref var postLateUpdate = ref UnityPlayerLoopTools.FindSystem(playerLoop, typeof(PostLateUpdate));
            UnityPlayerLoopTools.UpdateSubSystems(ref postLateUpdate, (systemsList) =>
            {
                _uGuiUpdateMarker.Apply<PostLateUpdate.PlayerUpdateCanvases>(systemsList);
                _particleSystemEndUpdateMarker.Apply<PostLateUpdate.ParticleSystemEndUpdateAll>(systemsList);
                _renderAndPresentMarker.Apply<PostLateUpdate.FinishFrameRendering>(systemsList);
            });
            
            PlayerLoop.SetPlayerLoop(playerLoop);
            
            // SRP marker
            RenderPipelineManager.beginContextRendering += SrpBeginFrameRendering;
            RenderPipelineManager.endContextRendering += SrpEndFrameRendering;
        }

        public void Stop()
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            UnityPlayerLoopTools.RemoveSystemRecursive(ref playerLoop, typeof(UnityPlayerLoop));
            PlayerLoop.SetPlayerLoop(playerLoop);
            
            RenderPipelineManager.beginContextRendering -= SrpBeginFrameRendering;
            RenderPipelineManager.endContextRendering -= SrpEndFrameRendering;
        }
    }
}
#endif
