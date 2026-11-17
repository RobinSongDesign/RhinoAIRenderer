using System;
using System.Threading.Tasks;
using Rhino;

namespace Chameleon.Rhino.Hosting
{
    /// <summary>Runs work on Rhino's UI thread, which RhinoCommon and Eto require, and awaits it from any thread.</summary>
    internal static class UiThread
    {
        /// <summary>Runs a function on the UI thread and returns its result.</summary>
        public static Task<T> InvokeAsync<T>(Func<T> work)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try
                {
                    completion.TrySetResult(work());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }));
            return completion.Task;
        }
    }
}
