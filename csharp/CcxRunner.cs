// =============================================================================
//  CcxRunner
//
//  Invokes the CalculiX solver (ccx) on a deck file and returns the parsed
//  modal results plus the paths to the output artefacts (.dat, .eig, .frd,
//  .sta, .cvg). The runner is synchronous: it blocks until ccx exits or the
//  timeout fires.
//
//  Usage:
//      var run = CcxRunner.Run(@"C:\runs\disc_modal.inp",
//                              ccxExecutable: @"C:\ccx\ccx_2.23.exe");
//      if (run.Success) {
//          foreach (var kv in run.Results.PhysicalModesByND())
//              Console.WriteLine($"ND {kv.Key}: {string.Join(", ", kv.Value)} Hz");
//      } else {
//          Console.WriteLine("CCX failed: " + run.ErrorMessage);
//      }
//
//  Notes:
//    - ccx takes the JOB NAME (no extension) as its single argument and
//      reads <jobname>.inp from the current working directory. The runner
//      sets WorkingDirectory to the deck's parent folder and passes
//      Path.GetFileNameWithoutExtension(deckPath) as the argument.
//    - "Success" requires exit code 0, a non-empty .dat file produced, and
//      no "*ERROR" / "nonpositive" markers in stdout. ccx is permissive
//      about exit codes when it encounters element-level errors, so the
//      stdout check is the more reliable failure signal.
//    - Output files are left in place after the run (no cleanup) so the
//      user can inspect / archive them.
// =============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LakeCore
{
    public static class CcxRunner
    {
        public class RunResult
        {
            public bool         Success;
            public int          ExitCode;
            public TimeSpan     Elapsed;
            public string       Stdout;
            public string       Stderr;
            public string       ErrorMessage;    // populated on failure

            // Output artefact paths (file may or may not exist depending on
            // how far ccx got; check File.Exists before reading).
            public string       DatPath;
            public string       FrdPath;
            public string       EigPath;
            public string       StaPath;
            public string       CvgPath;

            // Parsed modal results, populated on success when parseResults
            // was true (the default). Null on failure or when parseResults
            // was false.
            public ModalResults Results;
        }

        /// <summary>
        /// Run ccx on the given deck file and return the parsed results plus
        /// run metadata.
        /// </summary>
        /// <param name="deckPath">Path to the .inp deck file.</param>
        /// <param name="ccxExecutable">
        /// Full path to ccx_2.X(.exe). If null or empty, defaults to "ccx"
        /// and relies on PATH resolution.
        /// </param>
        /// <param name="timeoutSeconds">
        /// Maximum wall-clock time to wait for ccx. After this the process
        /// is killed and Success=false with a timeout message.
        /// </param>
        /// <param name="parseResults">
        /// When true (default), the .dat file is parsed into a ModalResults
        /// instance attached to RunResult.Results.
        /// </param>
        public static RunResult Run(
            string deckPath,
            string ccxExecutable = null,
            int    timeoutSeconds = 600,
            bool   parseResults  = true)
        {
            if (string.IsNullOrEmpty(deckPath))
                throw new ArgumentNullException(nameof(deckPath));
            if (!File.Exists(deckPath))
                throw new FileNotFoundException("Deck file not found", deckPath);

            string fullDeck   = Path.GetFullPath(deckPath);
            string workingDir = Path.GetDirectoryName(fullDeck);
            string jobName    = Path.GetFileNameWithoutExtension(fullDeck);
            string ccxExe     = string.IsNullOrEmpty(ccxExecutable) ? "ccx"
                                                                    : ccxExecutable;

            var result = new RunResult
            {
                DatPath = Path.Combine(workingDir, jobName + ".dat"),
                FrdPath = Path.Combine(workingDir, jobName + ".frd"),
                EigPath = Path.Combine(workingDir, jobName + ".eig"),
                StaPath = Path.Combine(workingDir, jobName + ".sta"),
                CvgPath = Path.Combine(workingDir, jobName + ".cvg"),
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = ccxExe,
                    Arguments              = jobName,
                    WorkingDirectory       = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };

                var stdoutBuf = new StringBuilder();
                var stderrBuf = new StringBuilder();

                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => {
                        if (e.Data != null) stdoutBuf.AppendLine(e.Data);
                    };
                    p.ErrorDataReceived  += (s, e) => {
                        if (e.Data != null) stderrBuf.AppendLine(e.Data);
                    };

                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    bool exited = p.WaitForExit(timeoutSeconds * 1000);
                    if (!exited)
                    {
                        try { p.Kill(); } catch { /* best-effort */ }
                        result.Stdout       = stdoutBuf.ToString();
                        result.Stderr       = stderrBuf.ToString();
                        result.Success      = false;
                        result.ErrorMessage = "CCX timed out after "
                                              + timeoutSeconds + "s";
                        return result;
                    }

                    // Block for async output handlers to flush.
                    p.WaitForExit();

                    result.ExitCode = p.ExitCode;
                    result.Stdout   = stdoutBuf.ToString();
                    result.Stderr   = stderrBuf.ToString();
                }

                // Success criteria -------------------------------------------
                bool hasDat        = File.Exists(result.DatPath)
                                  && new FileInfo(result.DatPath).Length > 0;
                bool hasErrorWord  = result.Stdout.IndexOf("*ERROR",
                                       StringComparison.Ordinal) >= 0
                                  || result.Stdout.IndexOf("nonpositive",
                                       StringComparison.Ordinal) >= 0;

                if (result.ExitCode == 0 && hasDat && !hasErrorWord)
                {
                    result.Success = true;
                    if (parseResults)
                        result.Results = ModalResults.ParseDat(result.DatPath);
                }
                else
                {
                    result.Success = false;
                    var msgs = new System.Collections.Generic.List<string>();
                    if (result.ExitCode != 0)
                        msgs.Add("exit code " + result.ExitCode);
                    if (!hasDat)
                        msgs.Add(".dat not produced or empty");
                    if (hasErrorWord)
                        msgs.Add("CCX reported errors in stdout");
                    result.ErrorMessage = string.Join("; ", msgs);
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // Typically "file not found" -- the ccx executable isn't on
                // PATH or the given path is wrong.
                result.Success      = false;
                result.ErrorMessage = "Failed to launch '" + ccxExe + "': "
                                      + ex.Message;
            }
            catch (Exception ex)
            {
                result.Success      = false;
                result.ErrorMessage = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                result.Elapsed = stopwatch.Elapsed;
            }

            return result;
        }
    }
}
