﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;

namespace TaranzaSoul
{
    public class Config
    {
        [JsonProperty("token")]
        public string Token { get; set; }
        [JsonProperty("prefixes")]
        public IEnumerable<string> PrefixList { get; set; } = new[]
        {
            ";",
            "!",
            "pls "
        };
        [JsonProperty("mention_trigger")]
        public bool TriggerOnMention { get; set; } = true;

        public static Config Load()
        {
            if (File.Exists("config.json"))
            {
                var json = File.ReadAllText("config.json");
                return JsonConvert.DeserializeObject<Config>(json);
            }
            var config = new Config();
            config.Save();
            throw new InvalidOperationException("configuration file created; insert token and restart.");
        }

        public void Save()
        {
            //var json = JsonConvert.SerializeObject(this);
            //File.WriteAllText("config.json", json);
            JsonStorage.SerializeObjectToFile(this, "config.json").Wait();
        }
    }
}