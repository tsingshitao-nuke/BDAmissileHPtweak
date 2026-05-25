using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

[KSPAddon(KSPAddon.Startup.MainMenu, true)]
public class MissileHPGenerator : MonoBehaviour
{
    private class MissileData
    {
        public float hp;
        public float armor;

        public MissileData(float hp, float armor)
        {
            this.hp = hp;
            this.armor = armor;
        }
    }

    private static readonly string PluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    private static readonly string ModRootDir = Directory.GetParent(PluginDir).FullName;

    private const string PatchFileName = "BDAmissileHPtweak_generated.cfg";
    private const string HashFileName = "BDAmissileHPtweak_generated.hash";
    private const string SettingsFileName = "settings.cfg";

    private static readonly string SettingsFile = Path.Combine(ModRootDir, SettingsFileName);
    private static readonly string PatchFilePath = Path.Combine(ModRootDir, PatchFileName);
    private static readonly string HashFilePath = Path.Combine(PluginDir, HashFileName);

    private float hpPerMass = 1000f;
    private float hpMassThreshold = 0.5f;
    private float minHP = 5f;

    private float armorPerMass = 20f;
    private float armorMassThreshold = 0.5f;
    private float minArmor = 10f;

    // 排除模块列表，零件只要含有其中任意模块就会被跳过
    private List<string> excludeModules = new List<string>();

    private void Awake()
    {
        LoadSettings();
    }

    private void Start()
    {
        string currentHash = ComputeSettingsHash();

        bool needRegenerate = !File.Exists(PatchFilePath);
        if (!needRegenerate && File.Exists(HashFilePath))
        {
            string savedHash = File.ReadAllText(HashFilePath).Trim();
            if (savedHash != currentHash)
            {
                Debug.Log("[BDAmissileHPtweak] Settings changed, deleting old patch...");
                File.Delete(PatchFilePath);
                needRegenerate = true;
            }
        }
        else if (!File.Exists(HashFilePath))
        {
            needRegenerate = true;
        }

        if (needRegenerate)
        {
            GenerateMMPatch();
            File.WriteAllText(HashFilePath, currentHash);
            Debug.Log("[BDAmissileHPtweak] Patch regenerated.");
        }
        else
        {
            Debug.Log("[BDAmissileHPtweak] Patch is up to date, skipping generation.");
        }

        Destroy(this);
    }

    private string ComputeSettingsHash()
    {
        if (!File.Exists(SettingsFile))
            return "default";

        string content = File.ReadAllText(SettingsFile);
        using (MD5 md5 = MD5.Create())
        {
            byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(content));
            StringBuilder sb = new StringBuilder();
            foreach (byte b in hashBytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    private void LoadSettings()
    {
        if (File.Exists(SettingsFile))
        {
            var settingsNode = ConfigNode.Load(SettingsFile);
            if (settingsNode != null)
            {
                var node = settingsNode.GetNode("MissileHPSettings");
                if (node != null)
                {
                    if (node.HasValue("hpPerMass"))
                        float.TryParse(node.GetValue("hpPerMass"), out hpPerMass);
                    if (node.HasValue("massThreshold"))
                        float.TryParse(node.GetValue("massThreshold"), out hpMassThreshold);
                    if (node.HasValue("minHP"))
                        float.TryParse(node.GetValue("minHP"), out minHP);

                    if (node.HasValue("armorPerMass"))
                        float.TryParse(node.GetValue("armorPerMass"), out armorPerMass);
                    if (node.HasValue("armorMassThreshold"))
                        float.TryParse(node.GetValue("armorMassThreshold"), out armorMassThreshold);
                    if (node.HasValue("minArmor"))
                        float.TryParse(node.GetValue("minArmor"), out minArmor);

                    // 解析排除模块列表（逗号分隔）
                    if (node.HasValue("excludeModules"))
                    {
                        string raw = node.GetValue("excludeModules");
                        excludeModules = raw.Split(',')
                                           .Select(s => s.Trim())
                                           .Where(s => !string.IsNullOrEmpty(s))
                                           .ToList();
                    }
                }
            }
            Debug.Log($"[BDAmissileHPtweak] Settings loaded. Exclude modules: {(excludeModules.Count > 0 ? string.Join(", ", excludeModules) : "none")}");
        }
        else
        {
            Debug.Log("[BDAmissileHPtweak] No settings.cfg found, using defaults.");
        }
    }

    private float CalculateHitPoints(float mass)
    {
        if (mass < hpMassThreshold)
            return minHP;
        float hp = mass * hpPerMass;
        return Mathf.Max(hp, minHP);
    }

    private float CalculateArmor(float mass)
    {
        if (mass < armorMassThreshold)
            return minArmor;
        float armor = mass * armorPerMass;
        return Mathf.Max(armor, minArmor);
    }

    private void GenerateMMPatch()
    {
        var missileParts = new Dictionary<string, MissileData>();

        List<AvailablePart> loadedParts = PartLoader.LoadedPartsList;
        Debug.Log($"[BDAmissileHPtweak] Scanning {loadedParts.Count} available parts...");

        int matched = 0;
        foreach (AvailablePart ap in loadedParts)
        {
            if (ap.partConfig == null || string.IsNullOrEmpty(ap.name))
                continue;

            ConfigNode[] modules = ap.partConfig.GetNodes("MODULE");

            // 检查是否含有排除模块
            bool excluded = false;
            foreach (ConfigNode m in modules)
            {
                string modName = m.GetValue("name");
                if (!string.IsNullOrEmpty(modName) && excludeModules.Contains(modName.Trim(), System.StringComparer.OrdinalIgnoreCase))
                {
                    excluded = true;
                    Debug.Log($"[BDAmissileHPtweak]   >>> EXCLUDED! {ap.name} (contains exclude module '{modName}')");
                    break;
                }
            }
            if (excluded) continue;

            // 检查是否有 MissileLauncher
            bool hasLauncher = false;
            foreach (ConfigNode modNode in modules)
            {
                if (modNode.GetValue("name") == "MissileLauncher")
                {
                    hasLauncher = true;
                    break;
                }
            }

            if (hasLauncher)
            {
                float mass = 0f;
                float.TryParse(ap.partConfig.GetValue("mass"), out mass);
                float hp = CalculateHitPoints(mass);
                float armor = CalculateArmor(mass);
                missileParts[ap.name] = new MissileData(hp, armor);
                matched++;
                Debug.Log($"[BDAmissileHPtweak]   >>> MATCH! {ap.name} (mass={mass:F3}) -> HP={hp}, Armor={armor}");
            }
        }

        Debug.Log($"[BDAmissileHPtweak] Found {matched} missile parts (after exclusions).");

        if (missileParts.Count == 0)
        {
            Debug.Log("[BDAmissileHPtweak] No missile parts found, patch not generated.");
            return;
        }

        if (!Directory.Exists(ModRootDir))
            Directory.CreateDirectory(ModRootDir);

        using (StreamWriter writer = new StreamWriter(PatchFilePath, false))
        {
            writer.WriteLine("// Auto-generated BDArmory hitpoints patch for missiles");
            writer.WriteLine("// Generated by BDAmissileHPtweak");

            foreach (var kvp in missileParts)
            {
                string partName = kvp.Key.Replace(' ', '?');

                writer.WriteLine($"@PART[{partName}]:NEEDS[BDArmory]:FOR[BDAmissileHPtweak]");
                writer.WriteLine("{");
                writer.WriteLine("\t%MODULE[HitpointTracker]");
                writer.WriteLine("\t{");
                writer.WriteLine($"\t\tmaxHitPoints = {kvp.Value.hp:F0}");
                writer.WriteLine($"\t\tArmorThickness = {kvp.Value.armor:F0}");
                writer.WriteLine("\t\tExplodeMode = Default");
                writer.WriteLine("\t}");
                writer.WriteLine("}");
            }
        }

        Debug.Log($"[BDAmissileHPtweak] Patch written to {PatchFilePath}");
    }
}