using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace NanoProfiler.Editor
{
    public static class TaskExtensions
    {
        public static async Task WithCancellation(this Task task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            await using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                    throw new OperationCanceledException(cancellationToken);
            }
            await task;
        }
        
        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            await using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                    throw new OperationCanceledException(cancellationToken);
            }
            return await task;
        }
        
        public static async ValueTask<T> WithCancellation<T>(this ValueTask<T> valueTask, CancellationToken cancellationToken)
        {
            if (valueTask.IsCompleted)
            {
                return await valueTask;
            }

            var tcs = new TaskCompletionSource<bool>();
            await using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (await Task.WhenAny(valueTask.AsTask(), tcs.Task) == tcs.Task)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        
            return await valueTask;
        }
        
        public static void SafeFireAndForget(this ValueTask task) => HandleSafeFireAndForgetValueTask(task);
        public static void SafeFireAndForget<T>(this ValueTask<T> task) => HandleSafeFireAndForgetValueTask(task);
        public static void SafeFireAndForget(this Task task) => HandleSafeFireAndForget(task);

        private static async void HandleSafeFireAndForget(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Task execution failed with {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static async void HandleSafeFireAndForgetValueTask(ValueTask task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Task execution failed with {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static async void HandleSafeFireAndForgetValueTask<T>(ValueTask<T> task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Task execution failed with {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}