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
        return obj!;
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
                var isHardmode = false;
                var artifactObj = new JObject();
                foreach (var property in ((JObject)artifact).Properties())
                {
                    if (ignoreEntries.Contains(property.Name)) continue;
                    isHardmode |= property.Name == "$type" && property.Value.Value<string>() == "HARDMODE, CobaltCore";

                    artifactObj[property.Name] = property.Value;
                }

                if (isHardmode) continue;

                artifactList.Add(artifactObj);
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
                "drawAnim",
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

        if (ship || characters)
        {
            var runConfig = new JObject();
            if (ship)
            {
                runConfig["selectedShip"] = obj.runConfig.selectedShip;
            }

            if (characters)
            {
                runConfig["selectedChars"] = obj.runConfig.selectedChars;
            }

            toExport.runConfig = runConfig;
        }

        var json = JsonConvert.SerializeObject(toExport);
        File.WriteAllText(path, json);
    }

    public static (ShipMetaData[], DataManager.ShipPath[]) ReadAll(string folder)
    {
        var shipMetaDatas = new List<ShipMetaData>();
        var shipPath = new List<DataManager.ShipPath>();

        // Search in the specified folder for .ccpj files
        var ccpjFiles = Directory.GetFiles(folder, "*.ccpj", SearchOption.AllDirectories);
        foreach (var file in ccpjFiles)
        {
            var json = File.ReadAllText(file);
            shipMetaDatas.Add(ReadMetaJson(json));
            shipPath.Add(new DataManager.ShipPath { Path = file });
        }

        // Search in zip files in the root folder
        var zipFiles = Directory.GetFiles(folder, "*.zip", SearchOption.TopDirectoryOnly);
        Console.WriteLine($"Found {zipFiles.Length} zip files");
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

                shipPath.Add(new DataManager.ShipPath { Path = file, ZipPath = entry.FullName });
            }
        }

        return (shipMetaDatas.ToArray(), shipPath.ToArray());
    }

    private static ShipMetaData ReadMetaJson(string json)
    {
        var obj = JsonConvert.DeserializeObject<dynamic>(json);
        dynamic? metaObj = obj?.__meta as JObject;

        return new ShipMetaData
        {
            Name = metaObj?.Name ?? "",
            Author = metaObj?.Author ?? "",
            Description = metaObj?.Description ?? "",
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
        var patch = JsonConvert.DeserializeObject<JObject>(patchJson)!;
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
                        profile["ship"] = ShipPatch((JObject)profile["ship"]!, (JObject)value);
                        continue;
                    case "artifacts":
                        var hardModeArtifact =
                            (profile["artifacts"] as JArray)?
                            .FirstOrDefault(
                                artifact =>
                                    ((JObject)artifact).TryGetValue("$type", out var aType) &&
                                    aType.Value<string>() == "HARDMODE, CobaltCore"
                            ) as JObject;
                        profile["artifacts"] = ArtifactPatch((JArray)property.Value, hardModeArtifact);
                        continue;
                    case "deck":
                        profile["deck"] = DeckPatch((JArray)property.Value);
                        continue;
                    case "runConfig":
                        profile["runConfig"] = RunConfig((JObject)profile["runConfig"]!, (JObject)value);
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
            profile["map"]!["currentLocation"] = "(2, 0)";
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

    private static JArray ArtifactPatch(JArray artifacts, JObject? hardMode)
    {
        var output = new JArray();
        if (hardMode != null)
        {
            output.Add(hardMode);
        }

        var upper = true;
        var pos = 86;

        foreach (var token in artifacts)
        {
            var obj = (JObject)token;
            var artifact = new JObject()
            {
                // NB: Type MUST be first!
                ["$type"] = null,
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
                // NB: Type MUST be first!
                ["$type"] = null,
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


    private static JObject RunConfig(JObject original, JObject ship)
    {
        var output = new JObject();

        foreach (var property in original.Properties())
        {
            output[property.Name] = property.Value;
        }

        foreach (var property in ship.Properties())
        {
            output[property.Name] = property.Value;
        }

        return output;
    }


    public static string? LoadJson(in DataManager.ShipPath shipPath)
    {
        if (!File.Exists(shipPath.Path))
        {
            Console.WriteLine($"File '{shipPath.Path}' no longer exists");
            return null;
        }

        if (shipPath.ZipPath == null)
        {
            return File.ReadAllText(shipPath.Path);
        }

        using var archive = ZipFile.OpenRead(shipPath.Path);
        var entry = archive.GetEntry(shipPath.ZipPath);
        if (entry == null)
        {
            Console.WriteLine($"Entry '{entry}' in '{shipPath.Path}' no longer exists");
            return null;
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return json;
    }
}