using System.Dynamic;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CobaltCoreEditor;

public static class ImportExport
{
    public static JObject Import(string path)
    {
        var json = File.ReadAllText(path);
        var obj = JsonConvert.DeserializeObject<JObject>(json);
        return obj;
    }

    public static void Export(
        string path,
        dynamic obj,
        bool ship,
        bool artifacts,
        bool deck,
        bool map,
        bool characters,
        ShipMetaData metaData
    )
    {
        // TODO: Hard mode artifact stuff
        // TODO: runConfig stuff
        dynamic toExport = new ExpandoObject();
        toExport.__meta = metaData;
        
        if (ship)
        {
            dynamic shipObject = new ExpandoObject();
            shipObject.key = obj.ship.key;
            shipObject.baseEnergy = obj.ship.baseEnergy;
            shipObject.baseDraw = obj.ship.baseDraw;
            shipObject.evadeMax = obj.ship.evadeMax;
            shipObject.baseDraw = obj.ship.baseDraw;
            shipObject.hpGainFromEliteKills = obj.ship.hpGainFromEliteKills;
            shipObject.hpGainFromBossKills = obj.ship.hpGainFromBossKills;
            shipObject.chassisUnder = obj.ship.chassisUnder;
            shipObject.chassisOver = obj.ship.chassisOver;
            shipObject.hull = obj.ship.hull;
            shipObject.hullMax = obj.ship.hullMax;
            shipObject.shieldMaxBase = obj.ship.shieldMaxBase;
            shipObject.heatMin = obj.ship.heatMin;
            shipObject.heatTrigger = obj.ship.heatTrigger;
            shipObject.overheatDamage = obj.ship.overheatDamage;

            var partList = new List<dynamic>();
            foreach (dynamic part in (JArray)obj.ship.parts)
            {
                dynamic partObject = new ExpandoObject();
                partObject.type = part.type;
                partObject.skin = part.skin;
                partObject.flip = part.flip;
                partObject.damageModifier = part.damageModifier;
                partObject.invincible = part.invincible;
                partList.Add(partObject);
            }

            shipObject.parts = partList;
            toExport.ship = shipObject;
        }

        if (artifacts)
        {
            var ignoreEntries = new HashSet<string>
            {
                "glowTime",
                "animation",
                "lastScreenPos",
            };

            var artifactList = new JArray();
            foreach (var artifact in (JArray)obj.artifacts)
            {
                var partObj = new JObject();
                foreach (var property in ((JObject)artifact).Properties())
                {
                    if (ignoreEntries.Contains(property.Name)) continue;
                    partObj[property.Name] = property.Value;
                }

                artifactList.Add(partObj);
            }

            toExport.artifacts = artifactList;
        }

        if (deck)
        {
            var ignoreEntries = new HashSet<string>
            {
                "pos",
                "targetPos",
                "hoverAnim",
                "isForeground",
            };

            var cardList = new JArray();
            foreach (var part in (JArray)obj.deck)
            {
                var cardObj = new JObject();
                foreach (var property in ((JObject)part).Properties())
                {
                    if (ignoreEntries.Contains(property.Name)) continue;
                    cardObj[property.Name] = property.Value;
                }

                cardList.Add(cardObj);
            }

            toExport.deck = cardList;
        }

        if (map)
        {
            toExport.map = obj.map;
        }

        if (characters)
        {
            toExport.characters = obj.characters;
        }

        var json = JsonConvert.SerializeObject(toExport);
        File.WriteAllText(path, json);
    }

    public static (ShipMetaData[], string[]) ReadAll(string folder)
    {
        var shipMetaDatas = new List<ShipMetaData>();
        var jsons = new List<string>();

        // Search in the specified folder for .ccpj files
        var ccpjFiles = Directory.GetFiles(folder, "*.ccpj", SearchOption.AllDirectories);
        foreach (var file in ccpjFiles)
        {
            var json = File.ReadAllText(file);
            shipMetaDatas.Add(ReadMetaJson(json));
            jsons.Add(json);
        }

        // Search in zip files in the root folder
        var zipFiles = Directory.GetFiles(folder, "*.zip", SearchOption.TopDirectoryOnly);
        foreach (var file in zipFiles)
        {
            using var archive = ZipFile.OpenRead(file);
            foreach (var entry in archive.Entries)
            {
                if (Path.GetExtension(entry.FullName) != ".ccpj") continue;
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                shipMetaDatas.Add(ReadMetaJson(json));
                jsons.Add(json);
            }
        }

        return (shipMetaDatas.ToArray(), jsons.ToArray());
    }

    private static ShipMetaData ReadMetaJson(string json)
    {
        var obj = JsonConvert.DeserializeObject<dynamic>(json);
        dynamic metaObj = (JObject)obj.__meta;
        return new ShipMetaData
        {
            Name = metaObj.Name,
            Author = metaObj.Author,
            Description = metaObj.Description,
        };
    }

    public static void Patch(string profilePath, string patchJson, bool resetPosition, bool backup)
    {
        if (backup)
        {
            var now = FileSystemHelper.Now();
            File.Copy(profilePath, $"{profilePath}.{now}.backup");
        }

        var profile = Import(profilePath)!;
        var patch = JsonConvert.DeserializeObject<JObject>(patchJson);
        foreach (var property in patch.Properties())
        {
            try
            {
                var value = property.Value;
                switch (property.Name)
                {
                    case "__meta":
                        continue;
                    case "ship":
                        profile["ship"] = ShipPatch((JObject)profile["ship"], (JObject)value);
                        continue;
                    case "artifacts":
                        profile["artifacts"] = ArtifactPatch((JArray)property.Value);
                        continue;
                    case "deck":
                        profile["deck"] = DeckPatch((JArray)property.Value);
                        continue;
                    case "map":
                    case "characters":
                    default:
                        profile[property.Name] = property.Value;
                        continue;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception Message: " + ex.Message);
                Console.WriteLine("Stack Trace: " + ex.StackTrace);
            }
        }

        if (resetPosition)
        {
            profile["map"]["currentLocation"] = "(0, 2)";
        }
        
        var json = JsonConvert.SerializeObject(profile);
        File.WriteAllText(profilePath, json);
    }

    private static JObject ShipPatch(JObject original, JObject ship)
    {
        var output = new JObject();

        foreach (var property in original.Properties())
        {
            output[property.Name] = property.Value;
        }

        foreach (var pair in ship)
        {
            if (pair.Key != "parts")
            {
                output[pair.Key] = pair.Value;
                continue;
            }

            var partsInput = (JArray)pair.Value!;
            var partsOutput = new JArray();
            foreach (var partIn in partsInput)
            {
                var partsInObj = (JObject)partIn;
                var partOut = new JObject
                {
                    ["flip"] = false,
                    ["damageModifier"] = "none",
                    ["damageModifierOverrideWhileActive"] = null,
                    ["invincible"] = false,
                    ["brittleIsHidden"] = false,
                    ["stunnable"] = false,
                    ["active"] = true,
                    ["offset"] = "(0, 0)",
                    ["intent"] = null,
                };
                foreach (var property in partsInObj.Properties())
                {
                    partOut[property.Name] = property.Value;
                }

                partsOutput.Add(partOut);
            }

            output["parts"] = partsOutput;
        }

        return output;
    }

    private static JArray ArtifactPatch(JArray artifacts)
    {
        var output = new JArray();

        var upper = true;
        var pos = 86;

        foreach (var token in artifacts)
        {
            var obj = (JObject)token;
            var artifact = new JObject()
            {
                ["glowTimer"] = 0.0f,
                ["animation"] = null,
                ["lastScreenPos"] = $"({pos}, {(upper ? 3 : 16)})",
            };
            foreach (var property in obj.Properties())
            {
                artifact[property.Name] = property.Value;
            }

            output.Add(artifact);

            if (upper)
            {
                upper = false;
            }
            else
            {
                upper = true;
                pos += 14;
            }
        }

        return output;
    }

    private static JArray DeckPatch(JArray deck)
    {
        var output = new JArray();

        var fakeId = 1000;

        foreach (var token in deck)
        {
            var obj = (JObject)token;
            var card = new JObject()
            {
                ["uuid"] = fakeId++,
                ["pos"] = "(40, 203)",
                ["targetPos"] = "(40, 203)",
                ["upgrade"] = "None",
            };
            foreach (var property in obj.Properties())
            {
                card[property.Name] = property.Value;
            }

            output.Add(card);
        }

        return output;
    }
}