using System;
using System.Diagnostics;

namespace DellFanController
{
    public class IpmiHelper
    {
        private static readonly object _lockObj = new object();
        private static string _ipmitoolPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Dell", "SysMgt", "bmc", "ipmitool.exe");

        public IpmiHelper(string unused) { }

        // 原版 cw1997 execute 方法: cmd.exe /c + 只读stdout
        private string Execute(string arguments)
        {
            lock (_lockObj)
            {
                try
                {
                    Process p = new Process();
                    p.StartInfo.FileName = "cmd.exe";
                    p.StartInfo.Arguments = "/c " + _ipmitoolPath + " " + arguments;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;
                    p.Start();
                    // 异步读避免死锁
                    string output = ""; string error = "";
                    System.Threading.Thread tOut = new System.Threading.Thread(() => { try { output = p.StandardOutput.ReadToEnd(); } catch { } }) { IsBackground = true };
                    System.Threading.Thread tErr = new System.Threading.Thread(() => { try { error = p.StandardError.ReadToEnd(); } catch { } }) { IsBackground = true };
                    tOut.Start(); tErr.Start();
                    if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } return "Error: timeout"; }
                    tOut.Join(1000); tErr.Join(1000);
                    if (!string.IsNullOrEmpty(error) && string.IsNullOrEmpty(output))
                        return "Error: " + error.Trim();
                    // 返回原始输出（含错误则合并）
                    string combined = "";
                    if (!string.IsNullOrEmpty(error)) combined = "STDERR: " + error.Trim() + "\n";
                    combined += (output ?? "");
                    System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dfc_raw.txt"), DateTime.Now.ToString("HH:mm:ss.fff") + " CMD:" + p.StartInfo.Arguments + "\r\nOUTPUT:" + (output ?? "") + "\r\nERR:" + (error ?? "") + "\r\n---\r\n");
                return combined;
                }
                catch (Exception ex) { return "Error: " + ex.Message; }
            }
        }

        public string DisableAutoMode(string ip, string user, string password)
        { return Execute("-I lanplus -H " + ip + " -U " + user + " -P " + password + " raw 0x30 0x30 0x01 0x00"); }

        public string ResetToAutoMode(string ip, string user, string password)
        { return Execute("-I lanplus -H " + ip + " -U " + user + " -P " + password + " raw 0x30 0x30 0x01 0x01"); }

        public string SetFanSpeed(string ip, string user, string password, int percent)
        {
            string r1 = DisableAutoMode(ip, user, password);
            string r2 = Execute("-I lanplus -H " + ip + " -U " + user + " -P " + password + " raw 0x30 0x30 0x02 0xff 0x" + percent.ToString("x2"));
            return r1 + "\n" + r2;
        }

        public double GetRawTemp(string ip, string user, string password)
        {
            string r = Execute("-I lanplus -H " + ip + " -U " + user + " -P " + password + " sensor");
            double val = ParseTemp(r);
            if (val > 0) return val;
            r = Execute("-I lan -H " + ip + " -U " + user + " -P " + password + " sensor");
            val = ParseTemp(r);
            if (val > 0) return val;
            return -1;
        }


        public double GetSensorValue(string ip, string user, string password, string sensorName)
        {
            string r = Execute("-I lanplus -H " + ip + " -U " + user + " -P " + password + " sensor");
            if (string.IsNullOrEmpty(r) || r.StartsWith("Error:") || r.StartsWith("STDERR:")) return -1;
            foreach (string line in r.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.TrimStart().StartsWith(sensorName))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length >= 2)
                    {
                        string numStr = parts[1].Trim();
                        string tnum = ""; bool dot = false;
                        foreach (char c in numStr) { if (char.IsDigit(c)) tnum += c; else if (c == '.' && !dot) { tnum += "."; dot = true; } else if (tnum.Length > 0 && !char.IsDigit(c) && c != '.') break; }
                        double val;
                        if (tnum.Length > 0 && double.TryParse(tnum, out val) && val > 0 && val < 120) return val;
                    }
                }
            }
            return -1;
        }

        private double ParseTemp(string output)
        {
            if (string.IsNullOrEmpty(output) || output.StartsWith("Error:") || output.StartsWith("STDERR:") || output.StartsWith("Unable")) return -1;
            foreach (string line in output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.IndexOf("Temp", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string[] parts = line.Split('|');
                    if (parts.Length >= 2)
                    {
                        string numStr = parts[1].Trim();
                        string tnum = ""; bool dot = false;
                        foreach (char c in numStr) { if (char.IsDigit(c)) tnum += c; else if (c == '.' && !dot) { tnum += "."; dot = true; } else if (tnum.Length > 0 && !char.IsDigit(c) && c != '.') break; }
                        double val;
                        if (tnum.Length > 0 && double.TryParse(tnum, out val) && val > 0 && val < 120) return val;
                    }
                }
            }
            return -1;
        }

        public string GetSensors(string ip, string user, string password)
        {
            string r = Execute("-I lanplus -H " + ip + " -U " + user + " -P " + password + " sensor");
            if (!string.IsNullOrEmpty(r) && !r.StartsWith("Error:") && !r.StartsWith("STDERR:") && r.Length > 20) return r;
            r = Execute("-I lan -H " + ip + " -U " + user + " -P " + password + " sensor");
            if (!string.IsNullOrEmpty(r) && !r.StartsWith("Error:") && !r.StartsWith("STDERR:") && r.Length > 20) return r;
            return "Error";
        }

        public string TestConnection(string ip, string user, string password)
        {
            string r = Execute("-I lanplus -H " + ip + " -U " + user + " -P " + password + " mc info");
            if (!string.IsNullOrEmpty(r) && !r.StartsWith("Error:") && !r.StartsWith("STDERR:")) return r;
            r = Execute("-I lan -H " + ip + " -U " + user + " -P " + password + " mc info");
            return r ?? "Error";
        }
    }
}