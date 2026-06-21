using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PalworldServerManager.Services
{
    public class SaveToolsService
    {
        private readonly string _pythonExe;
        private readonly string _pythonVersion;
        private readonly string _convertScript;

        public SaveToolsService()
        {
            _pythonExe = FindPython(out _pythonVersion);
            _convertScript = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "SaveTools",
                "convert_save.py"
            );
        }

        public bool IsAvailable => !string.IsNullOrEmpty(_pythonExe) && File.Exists(_convertScript);

        public string PythonVersion => _pythonVersion ?? "未检测到";

        public class ConvertResult
        {
            public bool Success { get; set; }
            public string? OutputPath { get; set; }
            public string? ErrorMessage { get; set; }
        }

        public async Task<ConvertResult> ConvertSavToJson(string savFilePath, bool minify = false, bool force = true)
        {
            if (!IsAvailable)
                return new ConvertResult { Success = false, ErrorMessage = "Python 或转换脚本不可用" };

            if (!File.Exists(savFilePath))
                return new ConvertResult { Success = false, ErrorMessage = $"文件不存在: {savFilePath}" };

            var args = $"\"{_convertScript}\" \"{savFilePath}\" --to-json";
            if (minify) args += " --minify-json";
            if (force) args += " --force";

            return await RunPython(args);
        }

        public async Task<ConvertResult> ConvertJsonToSav(string jsonFilePath, bool force = true)
        {
            if (!IsAvailable)
                return new ConvertResult { Success = false, ErrorMessage = "Python 或转换脚本不可用" };

            if (!File.Exists(jsonFilePath))
                return new ConvertResult { Success = false, ErrorMessage = $"文件不存在: {jsonFilePath}" };

            var args = $"\"{_convertScript}\" \"{jsonFilePath}\" --from-json";
            if (force) args += " --force";

            return await RunPython(args);
        }

        private async Task<ConvertResult> RunPython(string args)
        {
            var result = new ConvertResult();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _pythonExe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    result.Success = true;
                    result.OutputPath = stdout.Trim();
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = FormatPythonError(stderr, stdout);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private static string FormatPythonError(string stderr, string stdout)
        {
            var msg = string.IsNullOrEmpty(stderr) ? stdout : stderr;

            // Check for common Python version issue
            if (msg.Contains("TypeError") && msg.Contains("not subscriptable"))
            {
                return "Python 版本过低（需要 3.9+），请升级 Python 后重试。\n\n" + msg;
            }

            return msg;
        }

        private static string? FindPython(out string? version)
        {
            version = null;
            string[] candidates = { "python3", "python", "py" };

            foreach (var name in candidates)
            {
                try
                {
                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = name,
                        Arguments = "--version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });
                    if (proc == null) continue;
                    proc.WaitForExit(3000);
                    if (proc.ExitCode == 0)
                    {
                        var ver = proc.StandardOutput.ReadToEnd().Trim();
                        if (string.IsNullOrEmpty(ver))
                            ver = proc.StandardError.ReadToEnd().Trim();
                        version = ver;
                        return name;
                    }
                }
                catch
                {
                }
            }

            string[] commonPaths =
            {
                @"C:\Program Files\Python313\python.exe",
                @"C:\Program Files\Python312\python.exe",
                @"C:\Program Files\Python311\python.exe",
                @"C:\Program Files\Python310\python.exe",
                @"C:\Program Files\Python39\python.exe",
                @"C:\Users\%USERNAME%\AppData\Local\Programs\Python\Python313\python.exe",
                @"C:\Users\%USERNAME%\AppData\Local\Programs\Python\Python312\python.exe",
                @"C:\Users\%USERNAME%\AppData\Local\Programs\Python\Python311\python.exe",
                @"C:\Users\%USERNAME%\AppData\Local\Programs\Python\Python310\python.exe",
            };

            foreach (var raw in commonPaths)
            {
                var path = Environment.ExpandEnvironmentVariables(raw);
                if (File.Exists(path))
                {
                    try
                    {
                        using var proc = Process.Start(new ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = "--version",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        });
                        if (proc != null)
                        {
                            proc.WaitForExit(3000);
                            if (proc.ExitCode == 0)
                            {
                                var ver = proc.StandardOutput.ReadToEnd().Trim();
                                if (string.IsNullOrEmpty(ver))
                                    ver = proc.StandardError.ReadToEnd().Trim();
                                version = ver;
                                return path;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }
    }
}
