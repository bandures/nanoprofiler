#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;

namespace NanoProfiler
{
    public static class UnityPlayerLoopTools
    {
        /// <summary>
        /// Adds a new system into Scriptable Player Loop before the
        /// system with the specified type. Can be used together with
        /// UpdateSystem to minimize number of Array-List conversions.
        /// </summary>
        public static void InsertBefore(List<PlayerLoopSystem> systems, PlayerLoopSystem insertedSystem, Type insertionPoint)
        {
            InsertImpl(systems, insertedSystem, 0, insertionPoint);
        }
        
        /// <summary>
        /// Adds a new system into Scriptable Player Loop after the
        /// system with the specified type. Can be used together with
        /// UpdateSystem to minimize number of Array-List conversions.
        /// </summary>
        public static void InsertAfter(List<PlayerLoopSystem> systems, PlayerLoopSystem insertedSystem, Type insertionPoint)
        {
            InsertImpl(systems, insertedSystem, 1, insertionPoint);
        }
        
        /// <summary>
        /// Removes specified system type from all levels of the hierarchy
        /// of Scriptable Player Loop system 
        /// </summary>
        public static void RemoveSystemRecursive(ref PlayerLoopSystem root, Type removedSystem)
        {
            if (root.subSystemList == null)
                return;
            
            List<PlayerLoopSystem> systemsList = new(root.subSystemList);
            systemsList.RemoveAll((x) => x.type == removedSystem);
            root.subSystemList = systemsList.Count > 0 ? systemsList.ToArray() : null;

            for (var index = 0; index < root.subSystemList?.Length; index++)
                RemoveSystemRecursive(ref root.subSystemList[index], removedSystem);
        }
        
        /// <summary>
        /// Helper function if you need to insert multiple
        /// player loop systems at the same time.
        /// </summary>
        public static void UpdateSubSystems(ref PlayerLoopSystem root, Action<List<PlayerLoopSystem>> action)
        {
            List<PlayerLoopSystem> systemsList = new(root.subSystemList);
            action(systemsList);
            root.subSystemList = systemsList.ToArray();
        }

        /// <summary>
        /// Searches Player Loop System for a system with a specific type
        /// Nb! Only does it for the specified system, doesn't look into
        /// sub-systems.
        /// </summary>
        public static ref PlayerLoopSystem FindSystem(PlayerLoopSystem root, Type systemType)
        {
            for (int systemIdx = 0; systemIdx < root.subSystemList.Length; systemIdx++)
            {
                if (root.subSystemList[systemIdx].type != systemType)
                    continue;
                
                return ref root.subSystemList[systemIdx];
            }

            throw new ArgumentException("PlayerLoopSystem doesn't have system with type: " + systemType.Name);
        }

        public static void InjectSystemSimple(Type ownerClass, Type insertionPoint, PlayerLoopSystem.UpdateFunction updateDelegate)
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem updateSystem = new() { type = ownerClass, updateDelegate = updateDelegate };
            UpdateSubSystems(ref playerLoop, systemsList => InsertAfter(systemsList, updateSystem, insertionPoint));
            PlayerLoop.SetPlayerLoop(playerLoop);
        }

        public static void InjectSystemSimple(Type ownerClass, Type insertionPoint1, Type insertionPoint2, PlayerLoopSystem.UpdateFunction updateDelegate)
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            ref var nestedLoop = ref FindSystem(playerLoop, insertionPoint1);
            PlayerLoopSystem updateSystem = new() { type = ownerClass, updateDelegate = updateDelegate };
            UpdateSubSystems(ref nestedLoop, systemsList => InsertAfter(systemsList, updateSystem, insertionPoint2));
            PlayerLoop.SetPlayerLoop(playerLoop);
        }

        public static void RemoveSystemSimple(Type ownerClass)
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveSystemRecursive(ref playerLoop, ownerClass);
            PlayerLoop.SetPlayerLoop(playerLoop);
        }
        
        private static void InsertImpl(List<PlayerLoopSystem> systems, PlayerLoopSystem insertedSystem, int indexShift, Type insertionPoint)
        {
            for (int systemIdx = 0; systemIdx < systems.Count; systemIdx++)
            {
                if (systems[systemIdx].type == insertionPoint)
                {
                    int position = systemIdx + indexShift;
                    systems.Insert(position, insertedSystem);
                    return;
                }
            }

            throw new ArgumentException("PlayerLoopSystem doesn't have system with type: " + insertionPoint.Name);
        }
    }
}
#endif