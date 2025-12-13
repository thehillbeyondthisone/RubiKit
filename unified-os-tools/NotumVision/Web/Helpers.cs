using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NotumHUD.Web
{
    public static class ResponseHelper
    {
        public static Task SendJsonAsync(HttpListenerResponse response, object data)
        {
            string json = JsonConvert.SerializeObject(data);
            return SendStringAsync(response, json, "application/json; charset=utf-8");
        }

        public static Task SendStringAsync(HttpListenerResponse response, string content, string contentType = "text/plain; charset=utf-8")
        {
            byte[] buffer = Encoding.UTF8.GetBytes(content);
            response.ContentType = contentType;
            response.ContentLength64 = buffer.Length;
            return response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
    }

    public static class StatDefinitions
    {
        public static readonly string[] CoreStatNames = { "AddAllOff", "AddAllDef", "CriticalIncrease", "XPModifier" };
        public static readonly string[] DmgStatNames = { "ProjectileDamageModifier", "MeleeDamageModifier", "EnergyDamageModifier", "ChemicalDamageModifier", "RadiationDamageModifier", "ColdDamageModifier", "FireDamageModifier", "PoisonDamageModifier" };
        public static readonly (string StatName, string Label)[] AcStats = {
            ("ProjectileAC", "Proj"), ("MeleeAC", "Melee"), ("EnergyAC", "Energy"), ("ChemicalAC", "Chem"),
            ("RadiationAC", "Rad"), ("ColdAC", "Cold"), ("FireAC", "Fire"), ("PoisonAC", "Poison")
        };
        public static readonly string[] HpNowNames = { "Life", "Health", "CurrentHealth" };
        public static readonly string[] HpMaxNames = { "MaxHealth", "LifeMax", "MaxLife" };
        public static readonly string[] NanoNowNames = { "NanoEnergy", "Nano", "CurrentNano" };
        public static readonly string[] NanoMaxNames = { "MaxNanoEnergy", "MaxNano", "NanoMax" };
    }

    public static class StatLabels
    {
        private static readonly Dictionary<string, string> LabelMap = new Dictionary<string, string>
        {
            {"TwoHandedEdged","2HE"},{"OneHandedEdged","1HE"},{"TwoHandedBlunt","2HB"},{"OneHandedBlunt","1HB"},
            {"MeleeEnergy","ME"},{"RangedEnergy","RE"},{"FullAuto","Full Auto"},{"FlingShot","Fling"},
            {"AimedShot","Aimed Shot"},{"ComputerLiteracy","Comp Lit"},{"NanoCInit","Nano Init"},
            {"NanoProg","Nano Programming"},{"BodyDev","Body Dev"},{"DuckExp","Duck-Exp"},{"DodgeRanged","Dodge Ranged"},
            {"EvadeClsC","Evade Close"},{"AddAllOff","AAO"},{"AddAllDef","AAD"},{"CriticalIncrease","Crit+"},{"XPModifier","XP %"},
            {"MatterCreation","MC"},{"MatterMetamorphosis","MM"},{"BiologicalMetamorphosis","BM"},{"TimeAndSpace","TS"},
            {"PsychologicalModification","PM"},{"SensoryImprovement","SI"},{"SubMachineGun","SMG"},
            {"NanoResist","Nano Resist"},{"FirstAid","First Aid"},{"HealDelta","Heal Delta"},{"NanoDelta","Nano Delta"},{"Treatment","Treatment"}
        };

        public static string Get(string statName)
        {
            if (LabelMap.TryGetValue(statName, out var pretty)) return pretty;
            
            // Fallback for unmapped stats: insert spaces before capital letters.
            return System.Text.RegularExpressions.Regex.Replace(statName, "(\\B[A-Z])", " $1");
        }
    }
}