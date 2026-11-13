using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace XIVHubCompanion.Apps
{
    /// <summary>
    /// Crafting solver on the player's PC: aetherhold_solver.dll (raphael-rs, Apache-2.0, (c) KonaeAkira and
    /// contributors, built from the Aetherhold website's solver/ folder) next to the plugin. Same JSON in and out as
    /// POST /api/crafting/solve. When the DLL is missing or fails to load, callers use the server instead.
    /// </summary>
    internal static class LocalSolver
    {
        private const string DllName = "aetherhold_solver.dll";
        private static IntPtr _lib;
        private static bool _tried;
        private static SolveFn? _solve;
        private static FreeFn? _free;
        private static CancelFn? _cancel;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr SolveFn(IntPtr input, uint threads);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreeFn(IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CancelFn();

        /// <summary>Why the local solver is off (for the log), null when it works.</summary>
        public static string? LoadError { get; private set; }

        /// <summary>Loads the DLL from the plugin's folder once. False: use the server.</summary>
        public static bool Available(string pluginDir)
        {
            if (_tried) return _solve != null;
            _tried = true;
            try
            {
                var path = Path.Combine(pluginDir, DllName);
                if (!File.Exists(path)) { LoadError = "not shipped"; return false; }
                _lib = NativeLibrary.Load(path);
                _solve = Marshal.GetDelegateForFunctionPointer<SolveFn>(NativeLibrary.GetExport(_lib, "ah_solve"));
                _free = Marshal.GetDelegateForFunctionPointer<FreeFn>(NativeLibrary.GetExport(_lib, "ah_free"));
                _cancel = Marshal.GetDelegateForFunctionPointer<CancelFn>(NativeLibrary.GetExport(_lib, "ah_cancel"));
                return true;
            }
            catch (Exception ex)
            {
                // e.g. blocked by antivirus: fall back to the server.
                LoadError = ex.Message;
                _solve = null;
                return false;
            }
        }

        /// <summary>Leave most cores to the game: a quarter of them, 1 to 4.</summary>
        public static uint Threads => (uint)Math.Clamp(Environment.ProcessorCount / 4, 1, 4);

        /// <summary>Runs on a background thread. Returns the solver's JSON (an "error" field when it found nothing).</summary>
        public static Task<string> SolveAsync(string requestJson) => Task.Run(() =>
        {
            if (_solve == null || _free == null) throw new InvalidOperationException("Local solver not loaded");
            var input = Marshal.StringToCoTaskMemUTF8(requestJson);
            try
            {
                var outPtr = _solve(input, Threads);
                try { return Marshal.PtrToStringUTF8(outPtr) ?? "{\"error\":\"No answer\"}"; }
                finally { _free(outPtr); }
            }
            finally { Marshal.FreeCoTaskMem(input); }
        });

        /// <summary>Stops a running solve (plugin closing). The DLL stays loaded: unloading with rayon threads alive is unsafe.</summary>
        public static void Cancel()
        {
            try { _cancel?.Invoke(); } catch { }
        }
    }
}
