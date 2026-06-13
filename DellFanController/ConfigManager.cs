using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DellFanController
{
    [Serializable]
    public class CurvePoint
    {
        public double Temperature { get; set; }
        public double Speed { get; set; }
    }

    [Serializable]
    public class FanCurvePreset
    {
        public string Name { get; set; }
        public List<CurvePoint> Points { get; set; }
    }

    [Serializable]
    public class ServerConfig
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Ip { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public int SpeedPercent { get; set; }
        public List<CurvePoint> CurvePoints { get; set; }
        public List<FanCurvePreset> Presets { get; set; }
        public string ActivePresetName { get; set; }
        // 防抽风 + 紧急全速
        public int AutoIntervalSec { get; set; }
        public int TempChangeThreshold { get; set; }
        public int SpeedChangeThreshold { get; set; }
        public bool EmergencyEnabled { get; set; }
        public int EmergencyTemp { get; set; }
        public int EmergencyRecoverTemp { get; set; }

        public ServerConfig()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Name = "服务器";
            Ip = "192.168.1.100"; User = "root"; Password = "calvin";
            SpeedPercent = 30;
            CurvePoints = new List<CurvePoint> {
                new CurvePoint { Temperature = 25, Speed = 15 },
                new CurvePoint { Temperature = 45, Speed = 40 },
                new CurvePoint { Temperature = 65, Speed = 75 },
                new CurvePoint { Temperature = 85, Speed = 95 },
                new CurvePoint { Temperature = 100, Speed = 100 }
            };
            Presets = new List<FanCurvePreset>();
            ActivePresetName = "";
            AutoIntervalSec = 10;
            TempChangeThreshold = 3;
            SpeedChangeThreshold = 5;
            EmergencyEnabled = true;
            EmergencyTemp = 75;
            EmergencyRecoverTemp = 65;
        }
    }

    [Serializable]
    public class AppConfig
    {
        public List<ServerConfig> Servers { get; set; }
        public AppConfig() { Servers = new List<ServerConfig>(); }
    }

    public static class ConfigManager
    {
        private static readonly string ConfigFileName = "servers.json";
        public static string ConfigFilePath { get { return Path.Combine(Application.StartupPath, ConfigFileName); } }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var cfg = new JavaScriptSerializer().Deserialize<AppConfig>(json);
                    if (cfg.Servers != null)
                    {
                        var def = new ServerConfig();
                        foreach (var s in cfg.Servers)
                        {
                            if (s.CurvePoints == null || s.CurvePoints.Count < 2) s.CurvePoints = def.CurvePoints;
                            if (s.Presets == null) s.Presets = new List<FanCurvePreset>();
                            if (s.AutoIntervalSec <= 0) s.AutoIntervalSec = 10;
                            if (s.TempChangeThreshold <= 0) s.TempChangeThreshold = 3;
                            if (s.SpeedChangeThreshold <= 0) s.SpeedChangeThreshold = 5;
                            if (s.EmergencyTemp <= 0) s.EmergencyTemp = 75;
                            if (s.EmergencyRecoverTemp <= 0) s.EmergencyRecoverTemp = 65;
                        }
                        return cfg;
                    }
                }
            }
            catch { }
            return new AppConfig();
        }

        public static void Save(AppConfig config)
        {
            try { File.WriteAllText(ConfigFilePath, new JavaScriptSerializer().Serialize(config)); }
            catch { }
        }
    }
}