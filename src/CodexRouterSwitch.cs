using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Codex Router Switch")]
[assembly: System.Reflection.AssemblyDescription("Safe ON/OFF switch for a local Codex Router installation")]
[assembly: System.Reflection.AssemblyCompany("CodexRouterSwitch")]
[assembly: System.Reflection.AssemblyProduct("Codex Router Switch")]
[assembly: System.Reflection.AssemblyVersion("1.2.3.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.3.0")]

namespace CodexRouterSwitch
{
    internal sealed class AppPaths
    {
        public readonly string RouterRoot;
        public readonly string CodexHome;
        public readonly string RouterStateRoot;
        public readonly string RouterStartScript;
        public readonly string VisibleWrapper;
        public readonly string ConsoleStatePath;
        public readonly string ConfigManagerScript;
        public readonly string CatalogScript;
        public readonly string ServiceScript;
        public readonly string WindowsServiceScript;
        public readonly string RouterLog;

        public AppPaths()
        {
            string localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );
            string userProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile
            );

            RouterRoot = ReadOverride(
                "CODEX_ROUTER_SWITCH_ROUTER_ROOT",
                Path.Combine(localAppData, "codex-router")
            );
            CodexHome = ReadOverride(
                "CODEX_ROUTER_SWITCH_CODEX_HOME",
                Path.Combine(userProfile, ".codex")
            );
            RouterStateRoot = Path.Combine(CodexHome, "codex-router");
            RouterStartScript = Path.Combine(RouterStateRoot, "start-codex-router.cmd");
            VisibleWrapper = Path.Combine(RouterStateRoot, "router-switch-visible.cmd");
            ConsoleStatePath = Path.Combine(RouterStateRoot, "router-switch-console.json");
            ConfigManagerScript = Path.Combine(RouterRoot, "src", "config-manager.mjs");
            CatalogScript = Path.Combine(RouterRoot, "src", "catalog.mjs");
            ServiceScript = Path.Combine(RouterRoot, "src", "service.mjs");
            WindowsServiceScript = Path.Combine(
                RouterRoot,
                "src",
                "service-windows.mjs"
            );
            RouterLog = Path.Combine(RouterStateRoot, "router.log");
        }

        private static string ReadOverride(string name, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return String.IsNullOrWhiteSpace(value)
                ? Path.GetFullPath(fallback)
                : Path.GetFullPath(value);
        }
    }

    internal sealed class ProcessResult
    {
        public int ExitCode;
        public string StandardOutput;
        public string StandardError;
    }

    internal sealed class ConfigStatus
    {
        public string Mode;
        public string Model;
        public string ModelProvider;
        public bool LoginFree;
    }

    internal sealed class ServiceStatus
    {
        public bool Installed;
        public bool Loaded;
        public string State;
    }

    internal sealed class SwitchStatus
    {
        public string State;
        public bool ConfigOn;
        public bool Healthy;
        public string Model;
        public string ModelProvider;
        public string Message;
    }

    internal sealed class OperationResult
    {
        public bool Ok;
        public string State;
        public string Message;
        public readonly List<string> Warnings = new List<string>();
    }

    internal sealed class ConsoleRecord
    {
        public int ProcessId;
        public DateTime StartTimeUtc;
        public string Wrapper;
    }

    internal sealed class RouterController
    {
        private const int DefaultCommandTimeoutMs = 300000;
        private readonly AppPaths paths;
        private readonly JavaScriptSerializer json;
        private readonly string nodePath;

        public RouterController()
        {
            paths = new AppPaths();
            json = new JavaScriptSerializer();
            nodePath = ResolveNodePath();
        }

        public AppPaths Paths
        {
            get { return paths; }
        }

        public void AssertRouterFiles()
        {
            string[] required = new string[]
            {
                paths.ConfigManagerScript,
                paths.CatalogScript,
                paths.ServiceScript,
                paths.WindowsServiceScript
            };

            foreach (string path in required)
            {
                if (!File.Exists(path))
                {
                    throw new InvalidOperationException(
                        "Required Codex Router file is missing: " + path
                    );
                }
            }

            ProcessResult version = RunExternal(
                nodePath,
                new string[] { "--version" },
                15000
            );
            if (String.IsNullOrWhiteSpace(version.StandardOutput))
            {
                throw new InvalidOperationException("Node.js did not report a version.");
            }
        }

        public SwitchStatus GetStatus()
        {
            ConfigStatus config = GetConfigStatus();
            bool healthy = TestRouterHealth(1500);
            bool configOn = String.Equals(
                config.Mode,
                "router",
                StringComparison.OrdinalIgnoreCase
            );

            SwitchStatus status = new SwitchStatus();
            status.ConfigOn = configOn;
            status.Healthy = healthy;
            status.Model = config.Model;
            status.ModelProvider = config.ModelProvider;

            if (configOn && healthy)
            {
                status.State = "On";
                status.Message = "Router is enabled and healthy.";
            }
            else if (configOn)
            {
                status.State = "Degraded";
                status.Message =
                    "Router configuration is enabled, but the Router process is not healthy.";
            }
            else if (healthy)
            {
                status.State = "Orphaned";
                status.Message =
                    "Native Codex is active, but a Router process is still running.";
            }
            else
            {
                status.State = "Off";
                status.Message =
                    "Native Codex is active. Router credentials and settings are preserved.";
            }

            return status;
        }

        public Dictionary<string, object> SelfTest()
        {
            AssertRouterFiles();
            ConfigStatus config = GetConfigStatus();
            string rendered = RenderOfficialStartScript();
            if (rendered.IndexOf("src\\start.mjs", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException(
                    "The repository rendered an unrecognized Windows start script."
                );
            }

            Dictionary<string, object> result = new Dictionary<string, object>();
            result["ok"] = true;
            result["node"] = nodePath;
            result["configMode"] = config.Mode;
            result["model"] = config.Model;
            result["startScriptRender"] = "valid";
            result["mutationsPerformed"] = false;
            return result;
        }

        public OperationResult EnableVisibleRouter()
        {
            AssertRouterFiles();

            ConfigStatus initialConfig = GetConfigStatus();
            ServiceStatus initialService = GetServiceStatus();
            bool initialHealthy = TestRouterHealth(1500);
            bool runtimeChanged = false;
            bool configChangeAttempted = false;
            bool trackedConsoleWasRunning = false;
            string renderedStartScript = null;

            try
            {
                RunNode(paths.CatalogScript, new string[0], 60000);
                renderedStartScript = RenderOfficialStartScript();

                trackedConsoleWasRunning = StopTrackedConsole();
                runtimeChanged =
                    trackedConsoleWasRunning ||
                    initialService.Installed ||
                    initialHealthy;

                RunNode(
                    paths.ServiceScript,
                    new string[] { "uninstall" },
                    30000
                );
                runtimeChanged = runtimeChanged || initialService.Installed;

                if (!WaitForRouterHealth(false, 20))
                {
                    throw new InvalidOperationException(
                        "A Router process not owned by this switch still responds on port 4102."
                    );
                }

                WriteOfficialStartScript(renderedStartScript);
                WriteVisibleWrapper();

                configChangeAttempted = true;
                RunNode(
                    paths.ConfigManagerScript,
                    new string[] { "enable" },
                    15000
                );

                StartVisibleConsole();
                runtimeChanged = true;

                if (!WaitForRouterHealth(true, 300))
                {
                    throw new InvalidOperationException(
                        "Router did not become healthy within 300 seconds. Check " +
                        paths.RouterLog
                    );
                }

                OperationResult success = new OperationResult();
                success.Ok = true;
                success.State = "On";
                success.Message =
                    "Router is ON in a visible console. Restart Codex manually.";
                return success;
            }
            catch (Exception originalError)
            {
                List<string> rollbackErrors = new List<string>();

                if (runtimeChanged)
                {
                    try
                    {
                        StopTrackedConsole();
                    }
                    catch (Exception error)
                    {
                        rollbackErrors.Add("Could not stop the failed visible process: " + error.Message);
                    }
                }

                if (configChangeAttempted &&
                    !String.Equals(
                        initialConfig.Mode,
                        "router",
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    try
                    {
                        RunNode(
                            paths.ConfigManagerScript,
                            new string[] { "disable" },
                            15000
                        );
                    }
                    catch (Exception error)
                    {
                        rollbackErrors.Add(
                            "Could not restore native Codex configuration: " + error.Message
                        );
                    }
                }

                if (runtimeChanged)
                {
                    try
                    {
                        RestorePreviousRuntime(
                            initialService,
                            trackedConsoleWasRunning,
                            renderedStartScript
                        );
                    }
                    catch (Exception error)
                    {
                        rollbackErrors.Add(
                            "Could not restore the previous Router runtime: " + error.Message
                        );
                    }
                }

                string message = originalError.Message;
                if (rollbackErrors.Count > 0)
                {
                    message += " Rollback warning: " + String.Join(" ", rollbackErrors.ToArray());
                }
                throw new InvalidOperationException(message, originalError);
            }
        }

        public OperationResult DisableKeepSettings()
        {
            AssertRouterFiles();
            OperationResult result = new OperationResult();

            // Restore native Codex first. If this fails, leave the running Router
            // untouched so Codex is never left pointing at a dead local endpoint.
            RunNode(
                paths.ConfigManagerScript,
                new string[] { "disable" },
                15000
            );

            try
            {
                StopTrackedConsole();
            }
            catch (Exception error)
            {
                result.Warnings.Add(error.Message);
            }

            try
            {
                RunNode(
                    paths.ServiceScript,
                    new string[] { "uninstall" },
                    30000
                );
            }
            catch (Exception error)
            {
                result.Warnings.Add(error.Message);
            }

            TryDelete(paths.VisibleWrapper, result.Warnings);
            TryDelete(paths.ConsoleStatePath, result.Warnings);

            if (!WaitForRouterHealth(false, 20))
            {
                result.Warnings.Add(
                    "Native Codex was restored, but an untracked process still responds on port 4102."
                );
            }

            result.Ok = true;
            result.State = "Off";
            result.Message =
                "Router is OFF. Native Codex is active and Router settings are preserved.";
            if (result.Warnings.Count > 0)
            {
                result.Message += " Warning: " +
                    String.Join(" ", result.Warnings.ToArray());
            }
            return result;
        }

        private void RestorePreviousRuntime(
            ServiceStatus initialService,
            bool trackedConsoleWasRunning,
            string renderedStartScript
        )
        {
            if (initialService.Installed)
            {
                RunNode(
                    paths.ServiceScript,
                    new string[] { "install" },
                    330000
                );
                return;
            }

            if (trackedConsoleWasRunning)
            {
                if (String.IsNullOrEmpty(renderedStartScript))
                {
                    renderedStartScript = RenderOfficialStartScript();
                }
                WriteOfficialStartScript(renderedStartScript);
                WriteVisibleWrapper();
                StartVisibleConsole();
                if (!WaitForRouterHealth(true, 300))
                {
                    throw new InvalidOperationException(
                        "The previous visible Router runtime did not recover."
                    );
                }
            }
        }

        private ConfigStatus GetConfigStatus()
        {
            ProcessResult result = RunNode(
                paths.ConfigManagerScript,
                new string[] { "status" },
                15000
            );
            Dictionary<string, object> values = DeserializeObject(result.StandardOutput);
            ConfigStatus status = new ConfigStatus();
            status.Mode = ReadString(values, "mode");
            status.Model = ReadString(values, "model");
            status.ModelProvider = ReadString(values, "model_provider");
            status.LoginFree = ReadBoolean(values, "login_free");

            if (String.IsNullOrEmpty(status.Mode))
            {
                throw new InvalidOperationException(
                    "Codex Router returned an invalid configuration status."
                );
            }
            return status;
        }

        private ServiceStatus GetServiceStatus()
        {
            ProcessResult result = RunNode(
                paths.ServiceScript,
                new string[] { "status" },
                15000
            );
            Dictionary<string, object> values = DeserializeObject(result.StandardOutput);
            ServiceStatus status = new ServiceStatus();
            status.Installed = ReadBoolean(values, "installed");
            status.Loaded = ReadBoolean(values, "loaded");
            status.State = ReadString(values, "state");
            return status;
        }

        private string RenderOfficialStartScript()
        {
            ProcessResult result = RunNode(
                paths.WindowsServiceScript,
                new string[] { "render" },
                15000
            );
            if (result.StandardOutput.IndexOf(
                "src\\start.mjs",
                StringComparison.OrdinalIgnoreCase
            ) < 0)
            {
                throw new InvalidOperationException(
                    "The repository did not render a recognized Windows start script."
                );
            }
            return result.StandardOutput;
        }

        private void WriteOfficialStartScript(string contents)
        {
            Directory.CreateDirectory(paths.RouterStateRoot);
            AtomicWrite(paths.RouterStartScript, contents, new UTF8Encoding(false));
        }

        private void WriteVisibleWrapper()
        {
            Directory.CreateDirectory(paths.RouterStateRoot);
            string[] lines = new string[]
            {
                "@echo off",
                "title Codex Router - Visible Console",
                "echo.",
                "echo ============================================================",
                "echo  CODEX ROUTER IS RUNNING IN THIS VISIBLE WINDOW",
                "echo ============================================================",
                "echo.",
                "echo Keep this window open while the Router switch is ON.",
                "echo Use the EXE switch to turn Router OFF safely.",
                "echo Router log:",
                "echo " + paths.RouterLog,
                "echo.",
                "call \"" + paths.RouterStartScript.Replace("\"", "\"\"") + "\"",
                "echo.",
                "echo The Router process has stopped.",
                "echo Check router.log if this was unexpected.",
                "pause",
                ""
            };
            AtomicWrite(
                paths.VisibleWrapper,
                String.Join(Environment.NewLine, lines),
                Encoding.ASCII
            );
        }

        private void StartVisibleConsole()
        {
            if (!File.Exists(paths.RouterStartScript))
            {
                throw new InvalidOperationException(
                    "Router start script is missing: " + paths.RouterStartScript
                );
            }
            if (!File.Exists(paths.VisibleWrapper))
            {
                throw new InvalidOperationException(
                    "Visible Router wrapper is missing: " + paths.VisibleWrapper
                );
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec");
            if (String.IsNullOrEmpty(startInfo.FileName))
            {
                startInfo.FileName = "cmd.exe";
            }
            startInfo.Arguments = "/D /S /C \"\"" + paths.VisibleWrapper + "\"\"";
            startInfo.WorkingDirectory = paths.RouterRoot;
            startInfo.UseShellExecute = true;
            startInfo.WindowStyle = ProcessWindowStyle.Normal;

            Process process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException(
                    "The visible Router console could not be started."
                );
            }

            int processId = process.Id;
            try
            {
                ConsoleRecord record = new ConsoleRecord();
                record.ProcessId = processId;
                record.StartTimeUtc = process.StartTime.ToUniversalTime();
                record.Wrapper = paths.VisibleWrapper;
                SaveConsoleRecord(record);
            }
            catch
            {
                TryKillProcessTree(processId);
                throw;
            }
            finally
            {
                process.Dispose();
            }
        }

        private bool StopTrackedConsole()
        {
            ConsoleRecord record = LoadConsoleRecord();
            if (record == null)
            {
                return false;
            }

            try
            {
                Process process;
                try
                {
                    process = Process.GetProcessById(record.ProcessId);
                }
                catch (ArgumentException)
                {
                    return false;
                }

                using (process)
                {
                    if (!String.Equals(
                        process.ProcessName,
                        "cmd",
                        StringComparison.OrdinalIgnoreCase
                    ))
                    {
                        throw new InvalidOperationException(
                            "Refusing to stop PID " + record.ProcessId +
                            " because it is not the recorded command console."
                        );
                    }

                    double deltaSeconds = Math.Abs(
                        (
                            process.StartTime.ToUniversalTime() -
                            record.StartTimeUtc.ToUniversalTime()
                        ).TotalSeconds
                    );
                    if (deltaSeconds > 3.0)
                    {
                        throw new InvalidOperationException(
                            "Refusing to stop PID " + record.ProcessId +
                            " because the PID has been reused."
                        );
                    }

                    string commandLine = ReadProcessCommandLine(record.ProcessId);
                    if (String.IsNullOrEmpty(commandLine) ||
                        commandLine.IndexOf(
                            record.Wrapper,
                            StringComparison.OrdinalIgnoreCase
                        ) < 0)
                    {
                        throw new InvalidOperationException(
                            "Refusing to stop PID " + record.ProcessId +
                            " because its command line does not match this switch."
                        );
                    }
                }

                KillProcessTree(record.ProcessId);
                return true;
            }
            finally
            {
                TryDelete(paths.ConsoleStatePath, null);
            }
        }

        private string ReadProcessCommandLine(int processId)
        {
            string query =
                "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " +
                processId.ToString(CultureInfo.InvariantCulture);
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        object value = item["CommandLine"];
                        return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
                    }
                }
            }
            return null;
        }

        private void KillProcessTree(int processId)
        {
            string taskkill = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "taskkill.exe"
            );
            RunExternal(
                taskkill,
                new string[]
                {
                    "/PID",
                    processId.ToString(CultureInfo.InvariantCulture),
                    "/T",
                    "/F"
                },
                30000
            );
        }

        private void SaveConsoleRecord(ConsoleRecord record)
        {
            Dictionary<string, object> values = new Dictionary<string, object>();
            values["version"] = 1;
            values["pid"] = record.ProcessId;
            values["startTimeUtc"] = record.StartTimeUtc.ToString("o", CultureInfo.InvariantCulture);
            values["wrapper"] = record.Wrapper;
            AtomicWrite(
                paths.ConsoleStatePath,
                json.Serialize(values),
                new UTF8Encoding(false)
            );
        }

        private ConsoleRecord LoadConsoleRecord()
        {
            if (!File.Exists(paths.ConsoleStatePath))
            {
                return null;
            }

            string contents = File.ReadAllText(paths.ConsoleStatePath, Encoding.UTF8);
            Dictionary<string, object> values = DeserializeObject(contents);
            ConsoleRecord record = new ConsoleRecord();
            record.ProcessId = Convert.ToInt32(
                values["pid"],
                CultureInfo.InvariantCulture
            );
            record.StartTimeUtc = DateTime.Parse(
                ReadString(values, "startTimeUtc"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            );
            record.Wrapper = ReadString(values, "wrapper");

            if (record.ProcessId <= 0 || String.IsNullOrEmpty(record.Wrapper))
            {
                throw new InvalidOperationException(
                    "The saved Router console state is invalid."
                );
            }
            if (!String.Equals(
                Path.GetFullPath(record.Wrapper),
                Path.GetFullPath(paths.VisibleWrapper),
                StringComparison.OrdinalIgnoreCase
            ))
            {
                throw new InvalidOperationException(
                    "The saved Router console state belongs to another launcher."
                );
            }
            return record;
        }

        private bool TestRouterHealth(int timeoutMilliseconds)
        {
            HttpWebRequest request = null;
            HttpWebResponse response = null;
            try
            {
                request = (HttpWebRequest)WebRequest.Create(
                    "http://127.0.0.1:4102/health"
                );
                request.Method = "GET";
                request.Timeout = timeoutMilliseconds;
                request.ReadWriteTimeout = timeoutMilliseconds;
                request.KeepAlive = false;

                response = (HttpWebResponse)request.GetResponse();
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    Dictionary<string, object> values = DeserializeObject(reader.ReadToEnd());
                    return response.StatusCode == HttpStatusCode.OK &&
                        String.Equals(
                            ReadString(values, "service"),
                            "codex-router",
                            StringComparison.Ordinal
                        );
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                if (response != null)
                {
                    response.Dispose();
                }
            }
        }

        private bool WaitForRouterHealth(bool expected, int timeoutSeconds)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            do
            {
                if (TestRouterHealth(1500) == expected)
                {
                    return true;
                }
                Thread.Sleep(300);
            }
            while (DateTime.UtcNow < deadline);
            return false;
        }

        private ProcessResult RunNode(
            string script,
            string[] arguments,
            int timeoutMilliseconds
        )
        {
            List<string> allArguments = new List<string>();
            allArguments.Add(script);
            if (arguments != null)
            {
                allArguments.AddRange(arguments);
            }
            return RunExternal(nodePath, allArguments.ToArray(), timeoutMilliseconds);
        }

        private ProcessResult RunExternal(
            string filePath,
            string[] arguments,
            int timeoutMilliseconds
        )
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = filePath;
            startInfo.Arguments = JoinArguments(arguments);
            startInfo.WorkingDirectory = paths.RouterRoot;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.EnvironmentVariables["MODEL_ROUTER_TARGET"] = "codex";
            startInfo.EnvironmentVariables["CODEX_HOME"] = paths.CodexHome;
            startInfo.EnvironmentVariables["MODEL_ROUTER_STATE_DIR"] =
                paths.RouterStateRoot;
            startInfo.EnvironmentVariables["CODEX_ROUTER_STATE_DIR"] =
                paths.RouterStateRoot;

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        "Could not start process: " + filePath
                    );
                }

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    TryKillProcessTree(process.Id);
                    throw new TimeoutException(
                        "Command timed out: " + filePath + " " + startInfo.Arguments
                    );
                }

                string stdout = stdoutTask.Result;
                string stderr = stderrTask.Result;
                int exitCode = process.ExitCode;
                if (exitCode != 0)
                {
                    string detail = !String.IsNullOrWhiteSpace(stderr)
                        ? stderr.Trim()
                        : stdout.Trim();
                    if (String.IsNullOrWhiteSpace(detail))
                    {
                        detail = "exit code " + exitCode.ToString(CultureInfo.InvariantCulture);
                    }
                    throw new InvalidOperationException(
                        "Command failed: " + detail
                    );
                }

                ProcessResult result = new ProcessResult();
                result.ExitCode = exitCode;
                result.StandardOutput = stdout;
                result.StandardError = stderr;
                return result;
            }
        }

        private void TryKillProcessTree(int processId)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "taskkill.exe"
                );
                startInfo.Arguments =
                    "/PID " + processId.ToString(CultureInfo.InvariantCulture) +
                    " /T /F";
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                using (Process killer = Process.Start(startInfo))
                {
                    if (killer != null)
                    {
                        killer.WaitForExit(15000);
                    }
                }
            }
            catch
            {
                // The original timeout remains the primary error.
            }
        }

        private string ResolveNodePath()
        {
            string localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );
            string[] candidates = new string[]
            {
                Path.Combine(localAppData, "hermes", "node", "node.exe"),
                Path.Combine(localAppData, "Programs", "nodejs", "node.exe")
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string directory in pathValue.Split(Path.PathSeparator))
            {
                string clean = directory.Trim().Trim('"');
                if (String.IsNullOrEmpty(clean))
                {
                    continue;
                }
                string candidate = Path.Combine(clean, "node.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException(
                "Node.js was not found. Codex Router cannot be controlled."
            );
        }

        private Dictionary<string, object> DeserializeObject(string contents)
        {
            Dictionary<string, object> values =
                json.Deserialize<Dictionary<string, object>>(contents);
            if (values == null)
            {
                throw new InvalidOperationException("Expected a JSON object.");
            }
            return values;
        }

        private static string ReadString(
            Dictionary<string, object> values,
            string key
        )
        {
            object value;
            if (!values.TryGetValue(key, out value) || value == null)
            {
                return null;
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool ReadBoolean(
            Dictionary<string, object> values,
            string key
        )
        {
            object value;
            if (!values.TryGetValue(key, out value) || value == null)
            {
                return false;
            }
            if (value is bool)
            {
                return (bool)value;
            }
            bool parsed;
            return Boolean.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                out parsed
            ) && parsed;
        }

        private static void AtomicWrite(
            string path,
            string contents,
            Encoding encoding
        )
        {
            string directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = path + ".tmp." +
                Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) +
                "." + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, contents, encoding);
            try
            {
                if (File.Exists(path))
                {
                    string backup = path + ".replace-backup." + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.Replace(temporary, path, backup, true);
                    }
                    finally
                    {
                        if (File.Exists(backup))
                        {
                            File.Delete(backup);
                        }
                    }
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static void TryDelete(string path, List<string> warnings)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception error)
            {
                if (warnings != null)
                {
                    warnings.Add("Could not remove " + path + ": " + error.Message);
                }
            }
        }

        private static string JoinArguments(string[] arguments)
        {
            if (arguments == null || arguments.Length == 0)
            {
                return "";
            }
            string[] quoted = new string[arguments.Length];
            for (int index = 0; index < arguments.Length; index++)
            {
                quoted[index] = QuoteArgument(arguments[index] ?? "");
            }
            return String.Join(" ", quoted);
        }

        private static string QuoteArgument(string value)
        {
            if (value.Length > 0 &&
                value.IndexOfAny(new char[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                return value;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append('"');
            int backslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }
                builder.Append('\\', backslashes);
                backslashes = 0;
                builder.Append(character);
            }
            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        private Color borderColor = Color.FromArgb(209, 213, 219);
        private Color fillColor = Color.White;
        private int cornerRadius = 8;

        public Color BorderColor
        {
            get { return borderColor; }
            set
            {
                borderColor = value;
                Invalidate();
            }
        }

        public Color FillColor
        {
            get { return fillColor; }
            set
            {
                fillColor = value;
                Invalidate();
            }
        }

        public int CornerRadius
        {
            get { return cornerRadius; }
            set
            {
                cornerRadius = Math.Max(1, value);
                Invalidate();
            }
        }

        public RoundedPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true
            );
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using (GraphicsPath path = ModernUi.CreateRoundedRectangle(
                bounds,
                cornerRadius
            ))
            using (SolidBrush fill = new SolidBrush(fillColor))
            using (Pen border = new Pen(borderColor))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class StatusDot : Control
    {
        private Color dotColor = Color.FromArgb(96, 94, 92);

        public Color DotColor
        {
            get { return dotColor; }
            set
            {
                dotColor = value;
                Invalidate();
            }
        }

        public StatusDot()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint |
                ControlStyles.SupportsTransparentBackColor,
                true
            );
            BackColor = Color.Transparent;
            Size = new Size(14, 14);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brush = new SolidBrush(dotColor))
            {
                e.Graphics.FillEllipse(brush, 1, 1, Width - 2, Height - 2);
            }
        }
    }

    internal sealed class BusyLine : Control
    {
        private readonly System.Windows.Forms.Timer animationTimer;
        private int animationOffset;

        public bool Active
        {
            get { return animationTimer.Enabled; }
            set
            {
                animationTimer.Enabled = value;
                if (!value)
                {
                    animationOffset = 0;
                }
                Invalidate();
            }
        }

        public BusyLine()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true
            );
            Height = 3;
            TabStop = false;

            animationTimer = new System.Windows.Forms.Timer();
            animationTimer.Interval = 30;
            animationTimer.Tick += delegate
            {
                animationOffset += 8;
                Invalidate();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!Active)
            {
                e.Graphics.Clear(Color.White);
                return;
            }
            e.Graphics.Clear(Color.FromArgb(243, 242, 241));
            int segmentWidth = Math.Max(42, Width / 4);
            int travel = Width + segmentWidth;
            int x = animationOffset % travel - segmentWidth;
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(0, 95, 184)))
            {
                e.Graphics.FillRectangle(brush, x, 0, segmentWidth, Height);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animationTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
