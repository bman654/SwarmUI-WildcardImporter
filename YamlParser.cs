namespace Spoomples.Extensions.WildcardImporter
{
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using YamlDotNet.Serialization;

    public class YamlParser
    {
        private readonly IDeserializer _deserializer;

        public YamlParser()
        {
            _deserializer = new Deserializer();
        }

        public Dictionary<string, object> Parse(string yamlContent)
        {
            return _deserializer.Deserialize<Dictionary<string, object>>(yamlContent);
        }

        public static string SerializeObject(object obj)
        {
            try
            {
                return JsonConvert.SerializeObject(obj, Formatting.Indented);
            }
            catch
            {
                return obj.ToString();
            }
        }
    }
}
