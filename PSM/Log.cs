using PalworldServerManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace PalworldServerManager
{
    public class Log
    {
        public enum LogType
        {
            WSServer,
            MainConsole,
            PlayerData
        }


        /// <summary>
        /// 备份服务器重启、崩溃日志
        /// </summary>
        /// <param name="server"></param>
        /// <returns></returns>
        public static bool WriteServerCrashLog(Server server)
        {
            if (server == null)
            {
                //ShowLogError("写入崩溃日志失败：服务器实例为null");
                return false;
            }

            if (string.IsNullOrEmpty(server.Path))
            {
                //ShowLogError($"写入崩溃日志失败：[{server.ssmServerName ?? "未知服务器"}] 的路径未设置");
                return false;
            }

            try
            {
                string crashLogDir = Path.Combine(server.Path, "CrashLog", DateTime.Today.ToString("yyyy-MM-dd"), DateTime.Now.ToString("HH-mm-ss"));
                Directory.CreateDirectory(crashLogDir);

                CopyFileIfExists(Path.Combine(server.Path, "Pal", "Saved", "Logs", "Pal.log"), Path.Combine(crashLogDir, "Pal.log"));
                //ShowLogSuccess($"崩溃日志已保存至：{crashLogDir}");
                return true;
            }
            catch (Exception ex)
            {
                //ShowLogError($"写入崩溃日志失败：{ex.Message}");
                return false;
            }
        }

        private static void CopyFileIfExists(string sourcePath, string destinationPath)
        {
            if (File.Exists(sourcePath))
            {
                try
                {
                    File.Copy(sourcePath, destinationPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    //ShowLogError($"复制文件失败：{sourcePath} → {destinationPath}，错误：{ex.Message}");
                }
            }
            else
            {
                //ShowLogWarning($"文件不存在，跳过复制：{sourcePath}");
            }
        }
    }
}
